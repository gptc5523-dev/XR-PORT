#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""전체 빌드 오케스트레이터 — 선도부터 순서대로 재생성 (실물 공정 순서).
blender --background --factory-startup --python scripts/build_all.py
단계: [1] 선도(build_lines, 씬 초기화 포함) → [2] 철골(build_cage). 게이트는 밖에서:
python3 gates/gate_check.py"""
import os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STEPS = ["build_lines.py", "build_cage.py"]
for step in STEPS:
    path = os.path.join(ROOT, "scripts", step)
    print(f"[build_all] ── {step} ──")
    src = open(path, encoding="utf-8").read()
    exec(compile(src, path, "exec"), {"__name__": "__main__", "__file__": path})
print(f"[build_all] 완료 — {len(STEPS)} 단계")
