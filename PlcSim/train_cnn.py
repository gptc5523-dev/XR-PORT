#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""1D-CNN 비교군 (WBS 6.4) — 부스팅(train_ai.py)과 같은 데이터·같은 평가로 원신호 30초 창을 바로 학습한다.

입력: 판정 시각까지 30초(300행) × 채널(phys 16 / full +알람 10), 출력: 상태(3)·원인(18) 두 머리.
GPU 는 스트리밍(NVENC 세션)이 없을 때만 쓴다 — 에폭마다 확인하고 세션이 있으면 끝날 때까지 기다린다.
실행(서버): venv/bin/python PlcSim/train_cnn.py [--set phys|full] [--epochs 8] → <out>/cnn_<set>.pt · report_cnn_<set>.json
"""
import argparse, json, os, subprocess, sys, time
from multiprocessing import Pool

import numpy as np
import torch
import torch.nn as nn

D = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, D)
import gen_ai as G      # noqa: E402
import train_ai as T    # noqa: E402

N = 300


def channels(cols, fs):
    v = G.g.VMAX
    ch = [cols["GT_Velocity"] / v["gt"], cols["TR_Velocity"] / v["tr"], cols["HO_Velocity"] / v["ho"],
          cols["HO_Load"] / G.SWL, cols["ENV_Wind_Speed"] / G.WIND_STOP, cols["HO_Position"] / 40.0,
          cols["OP_Mode"] / 3.0, cols["SP_Mode"] / 3.0]
    ch += [cols[c] for c in ("GT_Running", "TR_Running", "HO_Running", "SP_TwistLock_Locked", "SP_Landed",
                             "SP_Container_Detected", "OP_Emergency_Stop", "COM_Link_Status")]
    if fs == "full":
        ch += [cols[c] for c in T.ALARM if c not in ("ALM_Latest_Severity", "ALM_Latest_Source")]
        ch += [cols["ALM_Latest_Severity"] / 3.0, cols["ALM_Latest_Source"] / 6.0]
    return np.stack(ch, 0).astype(np.float32)


def run_windows(job):
    path, step, keep, fs, seed = job
    cols = T.load_run(path); n = len(cols["y_state"])
    ends = np.arange(N - 1, n, step); ends = ends[cols["y_state"][ends] >= 0]
    rng = np.random.default_rng(seed)
    ends = ends[(cols["y_state"][ends] > 0) | (rng.random(len(ends)) < keep)]
    if len(ends) == 0: return None
    x = channels(cols, fs)
    win = np.lib.stride_tricks.sliding_window_view(x, N, axis=1)[:, ends - N + 1].transpose(1, 0, 2).copy()
    ys = cols["y_state"][ends].astype(np.int64)
    yc = np.array([T.CIDX[cols["y_cause"][i] or "none"] for i in ends], np.int64)
    return win, ys, yc


def split_windows(data, manifest, split, step, keep, fs, jobs):
    runs = [r for r in manifest["runs"] if r["split"] == split]
    jobs_ = [(os.path.join(data, r["csv"]), step, keep, fs, r["seed"]) for r in runs]
    with Pool(jobs) as p:
        parts = [x for x in p.imap(run_windows, jobs_, chunksize=4) if x is not None]
    return [np.concatenate([p[i] for p in parts]) for i in range(3)]


class Net(nn.Module):
    def __init__(self, cin):
        super().__init__()
        def blk(a, b, k, s): return nn.Sequential(nn.Conv1d(a, b, k, s, k // 2), nn.BatchNorm1d(b), nn.ReLU())
        self.body = nn.Sequential(blk(cin, 64, 7, 1), blk(64, 64, 5, 2), blk(64, 128, 5, 2), blk(128, 128, 3, 2), blk(128, 128, 3, 1))
        self.state = nn.Linear(256, 3); self.cause = nn.Linear(256, len(T.CAUSES))

    def forward(self, x):
        h = self.body(x); h = torch.cat([h.mean(2), h.amax(2)], 1)
        return self.state(h), self.cause(h)


def gpu_busy():
    try:
        out = subprocess.run(["nvidia-smi", "--query-gpu=encoder.stats.sessionCount", "--format=csv,noheader"],
                             capture_output=True, text=True, timeout=10).stdout.strip()
        return int(out.splitlines()[0]) > 0
    except Exception:
        return False


def wait_gpu():
    waited = 0
    while gpu_busy():
        if waited % 300 == 0: print(f"[train_cnn] 스트리밍 중(NVENC 세션) — GPU 대기 {waited}s", flush=True)
        time.sleep(30); waited += 30


def predict(net, X, dev, bs=4096):
    net.eval(); S, C = [], []
    with torch.no_grad():
        for i in range(0, len(X), bs):
            s, c = net(torch.from_numpy(X[i:i + bs]).to(dev))
            S.append(torch.softmax(s, 1).cpu().numpy()); C.append(torch.softmax(c, 1).cpu().numpy())
    return np.concatenate(S), np.concatenate(C)


def joint(ps, pc):
    st = ps.argmax(1); out = []
    for i in range(len(st)):
        if st[i] == 0: out.append(0); continue
        mask = T.CSTATE == st[i]; mask[0] = False
        out.append(int(np.argmax(np.where(mask, pc[i], -1))))
    return st, np.array(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default=os.path.join(D, "ai_data"))
    ap.add_argument("--out", default=os.path.expanduser("~/xrcrane-ai/models"))
    ap.add_argument("--set", default="phys", choices=("phys", "full"))
    ap.add_argument("--epochs", type=int, default=8)
    ap.add_argument("--jobs", type=int, default=16)
    args = ap.parse_args()
    torch.manual_seed(0); np.random.seed(0)
    dev = "cuda" if torch.cuda.is_available() else "cpu"
    manifest = json.load(open(os.path.join(args.data, "manifest.json"), encoding="utf-8"))
    t0 = time.time()
    Xtr, Str, Ctr = split_windows(args.data, manifest, "train", 20, 0.35, args.set, args.jobs)
    Xva, Sva, Cva = split_windows(args.data, manifest, "val", 40, 1.0, args.set, args.jobs)
    Xte, Ste, Cte = split_windows(args.data, manifest, "test", 20, 1.0, args.set, args.jobs)
    print(f"[train_cnn] {args.set} 창 train {len(Str):,} val {len(Sva):,} test {len(Ste):,} · 채널 {Xtr.shape[1]} · {time.time() - t0:.0f}s · {dev}", flush=True)
    net = Net(Xtr.shape[1]).to(dev)
    ws = torch.tensor(len(Str) / (3 * np.bincount(Str, minlength=3).clip(1)), dtype=torch.float32, device=dev)
    lc = np.bincount(Ctr[Str > 0], minlength=len(T.CAUSES)).clip(1)
    wc = torch.tensor(lc.sum() / (len(lc) * lc), dtype=torch.float32, device=dev)
    opt = torch.optim.AdamW(net.parameters(), lr=2e-3, weight_decay=1e-4)
    steps = args.epochs * ((len(Str) + 1023) // 1024)
    sched = torch.optim.lr_scheduler.OneCycleLR(opt, max_lr=2e-3, total_steps=steps)
    ce_s, ce_c = nn.CrossEntropyLoss(weight=ws), nn.CrossEntropyLoss(weight=wc)
    rng = np.random.default_rng(0)
    for ep in range(args.epochs):
        if dev == "cuda": wait_gpu()
        net.train(); perm = rng.permutation(len(Str)); tl = 0.0; t1 = time.time()
        for i in range(0, len(perm), 1024):
            b = perm[i:i + 1024]
            x = torch.from_numpy(Xtr[b]).to(dev); s = torch.from_numpy(Str[b]).to(dev); c = torch.from_numpy(Ctr[b]).to(dev)
            ls, lcause = net(x)
            loss = ce_s(ls, s) + (ce_c(lcause[s > 0], c[s > 0]) if (s > 0).any() else 0.0)
            opt.zero_grad(); loss.backward(); opt.step(); sched.step(); tl += float(loss)
        ps, _ = predict(net, Xva, dev)
        print(f"[train_cnn] 에폭 {ep + 1}/{args.epochs} 손실 {tl / (len(perm) / 1024):.4f} · val acc {(ps.argmax(1) == Sva).mean():.4f} · {time.time() - t1:.0f}s", flush=True)
    res = {"set": args.set, "device": dev, "epochs": args.epochs, "windows": {"train": len(Str), "val": len(Sva), "test": len(Ste)}}
    for name, X, S_, C_ in (("val", Xva, Sva, Cva), ("test", Xte, Ste, Cte)):
        ps, pc = predict(net, X, dev); st, ca = joint(ps, pc)
        res[name] = T.scores(S_, st); nz = S_ > 0; res[name]["cause_acc_nonnormal"] = round(float((ca[nz] == C_[nz]).mean()), 4)
    for b in ("kpi5", "kpi7", "ext"):
        d = os.path.join(args.data, "bench", b)
        cases = [json.loads(l) for l in open(os.path.join(d, "cases.jsonl"), encoding="utf-8")]
        X, act = [], []
        for c in cases:
            cols = T.load_run(os.path.join(d, c["clip"]))
            X.append(channels(cols, args.set)[:, -N:]); act.append(T.activity(cols, -1))
        ps, pc = predict(net, np.stack(X), dev); st, ca = joint(ps, pc)
        y = np.array([c["state"] for c in cases])
        summ = [G.summary(int(s), T.CAUSES[k] if s else "", G.CAUSES[T.CAUSES[k]][2] if s else 0, cs["crane"], a)
                for s, k, cs, a in zip(st, ca, cases, act)]
        r = T.scores(y, st); r["summary_acc"] = round(float(np.mean([s == c["summary_ref"] for s, c in zip(summ, cases)])), 4)
        if b == "ext":
            for prof in ("A", "B"):
                m_ = np.array([c["profile"] == prof for c in cases]); r["acc_" + prof] = round(float((y[m_] == st[m_]).mean()), 4)
        res[b] = r
    torch.save(net.state_dict(), os.path.join(args.out, f"cnn_{args.set}.pt"))
    with open(os.path.join(args.out, f"report_cnn_{args.set}.json"), "w", encoding="utf-8") as f: json.dump(res, f, ensure_ascii=False, indent=1)
    k7 = res["kpi7"]["per_class"]["이상"]
    print(f"[train_cnn] {args.set}: test acc {res['test']['acc']} · 지표5 상태 {res['kpi5']['acc']} 요약 {res['kpi5']['summary_acc']} · "
          f"지표7 이상 P {k7['P']} R {k7['R']} · ext A {res['ext']['acc_A']} B {res['ext']['acc_B']} · 전체 {time.time() - t0:.0f}s", flush=True)


if __name__ == "__main__":
    main()
