#!/usr/bin/env python3
"""부품 상태 판정 자체 시험 — python3 Server/test_parts.py (실패하면 AssertionError)."""
import json
from xrcrane_db import load_partmap, part_states, part_history

parts, book = load_partmap()
base = {"ALM_Active": 0, "ALM_Latest_Code": 0, "OP_Emergency_Stop": 0, "OP_Running": 1, "COM_Link_Status": 1,
        "SP_TwistLock_Locked": 1, "HO_Load": 12.5}


def view(crane="STS_Crane", **tags):
    return part_states(crane, {**base, **tags}, parts, book)


def state(v, pid):
    return next(p for p in v["parts"] if p["id"] == pid)


v = view()   # 알람 없음 → 전부 정상, 운전
assert v["worst"] == "정상" and v["op_state"] == "운전" and v["alarm"] is None and len(v["parts"]) == 31

assert book[1001]["part"] == "GT.BOGIE" and book[1001]["sev"] == 2
v = view(ALM_Active=1, ALM_Latest_Code=1001)   # 그룹 알람 → 보기 4개만 이상, 상위 GT 는 정상, 운영 이상
assert [p["id"] for p in v["parts"] if p["state"] == "이상"] == ["GT.BOGIE.L1", "GT.BOGIE.L2", "GT.BOGIE.S1", "GT.BOGIE.S2"]
assert state(v, "GT")["state"] == "정상" and v["op_state"] == "이상" and v["alarm"]["part_on_model"]

warn = next(c for c, e in sorted(book.items()) if e["sev"] == 1 and e.get("xr", True) and e.get("part"))
v = view(ALM_Active=1, ALM_Latest_Code=warn)   # 주의 알람 → 주의, 운영은 운전 그대로
assert v["worst"] == "주의" and v["op_state"] == "운전"

v = view(ALM_Active=0, ALM_Latest_Code=1001)   # 지난 코드가 남아도 비활성이면 정상
assert v["worst"] == "정상" and v["alarm"] is None

assert view(OP_Emergency_Stop=1)["op_state"] == "이상"

assert state(view(), "SP.TWL.L1")["tags"] == {"SP_TwistLock_Locked": 1, "SP_TwistLock_Unlocked": None, "SP_Landed": None}
assert "HO_Load" in state(view(), "HO.HEADBLOCK")["tags"] and "GT_Position" not in state(view(), "GT.BOGIE.L1")["tags"]

rtg = view("RTG 크레인_1", ALM_Active=1, ALM_Latest_Code=next(c for c, e in book.items() if e.get("part") == "ENV.ANEMO"))
assert rtg["kind"] == "RTG" and len(rtg["parts"]) == 30 and not rtg["alarm"]["part_on_model"]   # RTG 엔 풍속계 모델이 없다

rows = [{"t_ms": t, "raw": json.dumps({**base, **x})} for t, x in
        ((0, {}), (100, {"ALM_Active": 1, "ALM_Latest_Code": 1001}), (200, {"ALM_Active": 1, "ALM_Latest_Code": 1001}), (300, {}))]
h = part_history(rows, "STS_Crane", parts, book, "GT.BOGIE.L1")   # 바뀐 순간만: 100 이상, 300 정상
assert [(c["t_ms"], c["state"]) for c in h] == [(100, "이상"), (300, "정상")]
print("ok")
