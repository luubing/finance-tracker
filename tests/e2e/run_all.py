# -*- coding: utf-8 -*-
"""
E2E 总入口：依次运行 tests/e2e 下所有 *_test.py，汇总结果。

用法：
  python tests/e2e/run_all.py                 # 全部（先跑不依赖服务的基座自测）
  python tests/e2e/run_all.py login smoke     # 只跑文件名包含这些关键字的脚本
  python tests/e2e/run_all.py --list          # 只列出会运行的脚本

退出码：0 全部通过；1 有失败；2 环境未就绪（Web.Server 未启动，且要跑的脚本需要它）。
"""
import subprocess
import sys
import time
from pathlib import Path

from e2e_base import BASE_URL, require_server

HERE = Path(__file__).resolve().parent

#: 不依赖 Web.Server 的脚本（跳过服务就绪检查）
STANDALONE = {"e2e_base_test.py"}

#: 单个脚本的运行上限（秒）：最长的脚本（a11y_degradation_test）约 5 分钟，留足余量；
#: 某个脚本挂住（等一个永远不会出现的状态、浏览器卡死）时不让整轮回归无限等下去，按「超时失败」记入汇总
SCRIPT_TIMEOUT_S = 30 * 60

#: 脚本被判超时的退出码（汇总里显示 TIMEOUT，按失败处理）
TIMEOUT_CODE = 124


def discover(filters: "list[str]") -> "list[Path]":
    scripts = sorted(HERE.glob("*_test.py"), key=lambda p: (p.name not in STANDALONE, p.name))
    if filters:
        scripts = [s for s in scripts if any(f in s.name for f in filters)]
    return scripts


def main(argv: "list[str]") -> int:
    list_only = "--list" in argv
    filters = [a for a in argv if not a.startswith("--")]
    scripts = discover(filters)
    if not scripts:
        print("没有匹配的 E2E 脚本")
        return 1
    if list_only:
        print("\n".join(s.name for s in scripts))
        return 0

    if any(s.name not in STANDALONE for s in scripts):
        require_server()
        print(f"[ENV] Web.Server 就绪：{BASE_URL}")

    summary: list[tuple[str, int, float]] = []
    for script in scripts:
        print(f"\n######## {script.name} ########", flush=True)
        started = time.time()
        try:
            code = subprocess.run([sys.executable, str(script)], cwd=str(HERE), timeout=SCRIPT_TIMEOUT_S).returncode
        except subprocess.TimeoutExpired:
            print(f"[TIMEOUT] {script.name} 超过 {SCRIPT_TIMEOUT_S // 60} 分钟仍未结束，已终止（按失败处理；浏览器进程可能残留，必要时手动结束）", flush=True)
            code = TIMEOUT_CODE
        summary.append((script.name, code, time.time() - started))

    print("\n======== E2E 汇总 ========")
    for name, code, seconds in summary:
        status = {0: "PASSED", 2: "ENV", TIMEOUT_CODE: "TIMEOUT"}.get(code, "FAILED")
        print(f"{status:<7} {name}  ({seconds:.1f}s)")
    failed = [n for n, c, _ in summary if c != 0]
    print(f"\n{len(summary) - len(failed)}/{len(summary)} 通过")
    if not failed:
        return 0
    return 2 if all(c == 2 for _, c, _ in summary if c != 0) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
