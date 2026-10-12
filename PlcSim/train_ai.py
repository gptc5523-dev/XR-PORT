#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""상태 분류 모델 학습·평가 (WBS 6.4 · 지표 5·7) — 데이터셋 v2(gen_ai.py) 위에서.

창 분류: 판정 시각까지의 10초(100행) 창에서 특징을 뽑아 그 시각의 y_state(정상·주의·이상)와 y_cause(원인)를 맞힌다.
특징 세트 두 가지를 같이 잰다 — phys: 위치·속도·하중·풍속·운전 비트만(알람 열 전부 제외) / full: phys + 알람 열.
모델: scikit-learn HistGradientBoostingClassifier(클래스 균형 가중). 요약문 = 상태 + 원인 + 동작(규칙) → 기준셋 summary_ref 와 대조.

실행(서버): venv/bin/python PlcSim/train_ai.py [--data PlcSim/ai_data] [--out ~/xrcrane-ai/models] [--jobs 20]
산출: <out>/state_<set>.pkl · cause_<set>.pkl · report.json · report.md
"""
import argparse, csv, gzip, json, os, pickle, sys, time
from multiprocessing import Pool

import numpy as np
from numpy.lib.stride_tricks import sliding_window_view as swv
from sklearn.ensemble import HistGradientBoostingClassifier
from sklearn.metrics import confusion_matrix, precision_recall_fscore_support

D = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, D)
import gen_ai as G  # noqa: E402

W, S, L = 100, 30, 600          # 창 10초 · 짧은 창 3초 · 긴 창 60초(권상 최고속도처럼 긴 이동에서만 보이는 것)
STEP = {"train": 10, "val": 20, "test": 20}   # 판정 시각 간격(행)
CONT = ["GT_Velocity", "TR_Velocity", "HO_Velocity", "HO_Load", "ENV_Wind_Speed", "HO_Position"]
BIN = ["GT_Running", "TR_Running", "HO_Running", "GT_Brake_Released", "TR_Brake_Released", "HO_Brake_Released",
       "SP_TwistLock_Locked", "SP_Landed", "SP_Container_Detected", "OP_Power_On", "OP_Ready", "OP_Running",
       "OP_Standby", "OP_Emergency_Stop", "OP_AntiSway_Active", "COM_Link_Status"]
CAT = ["OP_Mode", "SP_Mode"]
ALARM = ["GT_Motor_Alarm", "TR_Motor_Alarm", "HO_Overload_Alarm", "HO_Snag_Alarm", "HO_Motor_Alarm", "SP_Mismatch_Alarm",
         "ENV_Wind_Alarm", "ALM_Active", "ALM_Latest_Severity", "ALM_Latest_Source"]
CAUSES = ["none"] + list(G.CAUSES)
CIDX = {c: i for i, c in enumerate(CAUSES)}
CSTATE = np.array([0] + [G.CSTATE[c] for c in G.CAUSES])
CODES = sorted({v[2] for v in G.CAUSES.values() if v[2]})


def trailing(mask, ends, cap):
    """판정 시각에서 거꾸로 mask 가 이어진 행 수(최대 cap) — 잠금 지연·통신 끊김 지속처럼 '얼마나 오래'를 본다."""
    pos = np.arange(len(mask)); last_off = np.maximum.accumulate(np.where(mask, -1, pos))
    return np.minimum(pos - last_off, cap)[ends].astype(np.float32)


def features(cols, ends):
    """cols: 열 이름 → np 배열. 반환: (phys 특징, 알람 특징, 이름들)."""
    f, names = [], []
    def add(v, n): f.append(np.asarray(v, np.float32)); names.append(n)
    for c in CONT:
        x = cols[c]; ww = swv(x, W)[ends - W + 1]; ss = swv(x, S)[ends - S + 1]
        add(x[ends], c + ".last"); add(ww.mean(1), c + ".mean"); add(ww.std(1), c + ".std")
        add(ww.min(1), c + ".min"); add(ww.max(1), c + ".max"); add(ss.mean(1), c + ".mean3")
        add(ss.max(1), c + ".max3"); add(ww[:, -1] - ww[:, 0], c + ".delta")
        d = np.abs(np.diff(ww, axis=1)); add(d.max(1), c + ".jump")
    for c in ("GT_Velocity", "TR_Velocity", "HO_Velocity"):
        x = np.abs(cols[c]); pad = np.concatenate([np.zeros(L - 1, np.float32), x])
        add(swv(pad, L)[ends].max(1), c + ".absmax60")
    ho_max = swv(np.concatenate([np.zeros(L - 1, np.float32), np.abs(cols["HO_Velocity"])]), L)[ends].max(1)
    add(ho_max / G.g.VMAX["ho"], "HO.speed_ratio60")
    add(cols["HO_Load"][ends] / G.SWL, "HO.load_ratio")
    add(swv(cols["HO_Load"], S)[ends - S + 1].max(1) / G.SWL, "HO.load_ratio_max3")
    landed_unlocked = (cols["SP_Landed"] > 0.5) & (cols["SP_TwistLock_Locked"] < 0.5)
    add(trailing(landed_unlocked, ends, 200), "SP.landed_unlocked_run")
    add(trailing(cols["COM_Link_Status"] < 0.5, ends, 400), "COM.down_run")
    still = (np.abs(cols["GT_Velocity"]) < 0.01) & (np.abs(cols["TR_Velocity"]) < 0.01) & (np.abs(cols["HO_Velocity"]) < 0.01)
    add(trailing(still, ends, 600), "AX.still_run")
    for c in BIN:
        x = cols[c]; add(x[ends], c + ".last"); add(swv(x, W)[ends - W + 1].mean(1), c + ".mean")
    for c in CAT: add(cols[c][ends], c + ".last")
    phys = np.stack(f, 1); pn = list(names)
    a, an = [], []
    for c in ALARM:
        x = cols[c]; a.append(x[ends]); an.append(c + ".last"); a.append(swv(x, W)[ends - W + 1].max(1)); an.append(c + ".max")
    code = cols["ALM_Latest_Code"][ends]
    for k in CODES: a.append((code == k).astype(np.float32)); an.append(f"ALM_{k}")
    return phys, np.stack(a, 1).astype(np.float32), pn, an


def load_run(path):
    with gzip.open(path, "rt", encoding="utf-8") as f:
        rd = csv.reader(f); head = next(rd); rows = list(rd)
    ix = {h: i for i, h in enumerate(head)}
    num = CONT + BIN + CAT + ALARM + ["ALM_Latest_Code", "y_state"]
    cols = {c: np.array([r[ix[c]] for r in rows], dtype=np.float32) for c in num}
    cols["y_cause"] = [r[ix["y_cause"]] for r in rows]; cols["y_activity"] = [r[ix["y_activity"]] for r in rows]
    return cols


def run_feats(job):
    path, step = job
    cols = load_run(path); n = len(cols["y_state"])
    if n < L: return None
    ends = np.arange(W - 1, n, step)
    ends = ends[cols["y_state"][ends] >= 0]
    if len(ends) == 0: return None
    phys, alarm, _, _ = features(cols, ends)
    ys = cols["y_state"][ends].astype(np.int8)
    yc = np.array([CIDX[cols["y_cause"][i] or "none"] for i in ends], np.int8)
    return phys, alarm, ys, yc


def split_matrix(data, manifest, split, jobs):
    runs = [os.path.join(data, r["csv"]) for r in manifest["runs"] if r["split"] == split]
    with Pool(jobs) as p:
        parts = [x for x in p.imap(run_feats, [(r, STEP[split]) for r in runs], chunksize=4) if x is not None]
    return tuple(np.concatenate([p[i] for p in parts]) for i in range(4))


def bench_matrix(data, name):
    d = os.path.join(data, "bench", name)
    cases = [json.loads(l) for l in open(os.path.join(d, "cases.jsonl"), encoding="utf-8")]
    P, A, act = [], [], []
    for c in cases:
        cols = load_run(os.path.join(d, c["clip"]))
        n = len(cols["y_state"])
        e = np.array([len(cols["y_state"]) - 1])
        ph, al, _, _ = features(cols, e); P.append(ph[0]); A.append(al[0])
        act.append(activity(cols, -1))
    return cases, np.stack(P), np.stack(A), act


def activity(cols, i):
    """동작(요약문용) — 규칙: 잠금+움직임 = 이송, 움직임 = 공차 이동, 안착·감지 = 체결·해제, 그 밖 = 대기."""
    mv = any(abs(cols[c][i]) > 0.02 for c in ("GT_Velocity", "TR_Velocity", "HO_Velocity"))
    if cols["SP_TwistLock_Locked"][i] > 0.5 and mv: return "carry"
    if mv: return "empty_move"
    if cols["SP_Landed"][i] > 0.5 or cols["SP_Container_Detected"][i] > 0.5: return "handling"
    return "idle"


def balance(ys, yc, rng, normal_keep):
    keep = (ys != 0) | (rng.random(len(ys)) < normal_keep)
    return np.flatnonzero(keep)


def fit(X, y, seed):
    m = HistGradientBoostingClassifier(max_iter=400, learning_rate=0.1, max_leaf_nodes=63, min_samples_leaf=40,
                                       l2_regularization=1.0, class_weight="balanced", early_stopping=True,
                                       validation_fraction=0.1, n_iter_no_change=20, random_state=seed)
    return m.fit(X, y)


def joint(state_m, cause_m, X):
    """상태와 원인이 어긋나지 않게 — 예측 상태에 맞는 원인 중 확률이 가장 높은 것(정상이면 none)."""
    st = state_m.predict(X); pc = cause_m.predict_proba(X)
    full = np.zeros((len(X), len(CAUSES))); full[:, cause_m.classes_] = pc
    out = []
    for i in range(len(X)):
        if st[i] == 0: out.append(0); continue
        mask = CSTATE == st[i]; mask[0] = False
        out.append(int(np.argmax(np.where(mask, full[i], -1))))
    return st, np.array(out)


def scores(y, p):
    pr, rc, f1, sup = precision_recall_fscore_support(y, p, labels=[0, 1, 2], zero_division=0)
    return {"acc": round(float((y == p).mean()), 4), "macro_f1": round(float(f1.mean()), 4),
            "per_class": {G.STATE_KO[k]: {"P": round(float(pr[k]), 4), "R": round(float(rc[k]), 4), "n": int(sup[k])} for k in range(3)},
            "confusion": confusion_matrix(y, p, labels=[0, 1, 2]).tolist()}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default=os.path.join(D, "ai_data"))
    ap.add_argument("--out", default=os.path.expanduser("~/xrcrane-ai/models"))
    ap.add_argument("--jobs", type=int, default=max(1, (os.cpu_count() or 2) - 4))
    ap.add_argument("--normal-keep", type=float, default=0.35, help="학습 정상 창 표본 비율(나머지 클래스는 전부)")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    manifest = json.load(open(os.path.join(args.data, "manifest.json"), encoding="utf-8"))
    t0 = time.time()
    mats = {sp: split_matrix(args.data, manifest, sp, args.jobs) for sp in ("train", "val", "test")}
    _, _, pn, an = features({c: np.zeros(L, np.float32) for c in CONT + BIN + CAT + ALARM + ["ALM_Latest_Code"]}, np.array([L - 1]))
    print(f"[train_ai] 특징 추출 {time.time() - t0:.0f}s · train {len(mats['train'][2]):,} · val {len(mats['val'][2]):,} · "
          f"test {len(mats['test'][2]):,} 창 · 특징 phys {len(pn)} + 알람 {len(an)}", flush=True)
    rng = np.random.default_rng(0)
    trP, trA, trS, trC = mats["train"]
    idx = balance(trS, trC, rng, args.normal_keep)
    benches = {b: bench_matrix(args.data, b) for b in ("kpi5", "kpi7", "ext")}
    report = {"date": time.strftime("%Y-%m-%d %H:%M"), "windows": {k: int(len(v[2])) for k, v in mats.items()},
              "train_used": int(len(idx)), "features": {"phys": pn, "alarm": an}, "sets": {}}
    for fs in ("phys", "full"):
        Xtr = trP[idx] if fs == "phys" else np.hstack([trP[idx], trA[idx]])
        t1 = time.time()
        sm = fit(Xtr, trS[idx], 1)
        nz = trS[idx] > 0
        cm = fit(Xtr[nz], trC[idx][nz], 2)
        res = {"fit_s": round(time.time() - t1), "iters": {"state": int(sm.n_iter_), "cause": int(cm.n_iter_)}}
        for sp in ("val", "test"):
            P, A, S_, C_ = mats[sp]; X = P if fs == "phys" else np.hstack([P, A])
            st, ca = joint(sm, cm, X)
            res[sp] = scores(S_, st)
            nzm = S_ > 0
            res[sp]["cause_acc_nonnormal"] = round(float((ca[nzm] == C_[nzm]).mean()), 4)
        for b, (cases, P, A, act) in benches.items():
            X = P if fs == "phys" else np.hstack([P, A])
            st, ca = joint(sm, cm, X)
            y = np.array([c["state"] for c in cases])
            summ = [G.summary(int(s), CAUSES[c] if s else "", G.CAUSES[CAUSES[c]][2] if s else 0, cs["crane"], a)
                    for s, c, cs, a in zip(st, ca, cases, act)]
            ok = np.array([s == c["summary_ref"] for s, c in zip(summ, cases)])
            r = scores(y, st); r["summary_acc"] = round(float(ok.mean()), 4)
            if b == "ext":
                for prof in ("A", "B"):
                    m_ = np.array([c["profile"] == prof for c in cases])
                    r["acc_" + prof] = round(float((y[m_] == st[m_]).mean()), 4)
            hard = np.array([c["hard"] for c in cases]); r["acc_hard"] = round(float((y[hard] == st[hard]).mean()), 4)
            res[b] = r
            if b == "kpi5" and fs == "phys":
                res["kpi5_errors"] = [{"id": c["id"], "ref": c["summary_ref"], "pred": s} for s, c, o in zip(summ, cases, ok) if not o][:40]
        report["sets"][fs] = res
        with open(os.path.join(args.out, f"state_{fs}.pkl"), "wb") as f: pickle.dump(sm, f)
        with open(os.path.join(args.out, f"cause_{fs}.pkl"), "wb") as f: pickle.dump(cm, f)
        k5, k7 = res["kpi5"], res["kpi7"]["per_class"]["이상"]
        print(f"[train_ai] {fs}: 학습 {res['fit_s']}s · test acc {res['test']['acc']} · 지표5 상태 {k5['acc']} 요약 {k5['summary_acc']} · "
              f"지표7 이상 P {k7['P']} R {k7['R']} · ext A {res['ext']['acc_A']} B {res['ext']['acc_B']}", flush=True)
    with open(os.path.join(args.out, "report.json"), "w", encoding="utf-8") as f: json.dump(report, f, ensure_ascii=False, indent=1)
    print(f"[train_ai] 끝 {time.time() - t0:.0f}s → {args.out}")


if __name__ == "__main__":
    main()
