# -*- coding: utf-8 -*-
"""
假云端：E2E 用的最小「云端」，让 Web.Server 看到一个「云端可用」的环境。

为什么需要它
------------
共享账本的成员管理（邀请 / 修改角色 / 移除 / 转让所有权 / 退出）与邀请的接受 / 拒绝，都是「先调云端接口，
再把云端的成员关系拉回本地缓存」的流程。Web.Server 单独运行时没有云端（真实云端是 FinanceTracker.Api + PostgreSQL），
这些流程只能验证到「云端不可用时的报错」。这里用一个内存实现把另一半补上：

  - 在本机 ``http://127.0.0.1:5271``（``E2E_CLOUD_PORT`` 可改）起一个 HTTP 服务，只实现账本成员相关的接口（外加下文的导入与同步桩）；
  - ``server.ps1`` 启动 Web.Server 时把 ``ApiSettings__BaseUrl`` 指向它——所以 E2E 永远不会去碰开发者本机真实的
    Api / 数据库（登录会向云端换令牌，E2E 的随机手机号也不会写进真实库）；
  - 状态码 / 消息与真实接口（server/FinanceTracker.Api/Controllers 里的 LedgerMembersController、AuthController、
    SyncController.PullLedgerMembers）保持一致：权限不足 403、参数问题 400、不存在 404、冲突 409，响应体都是 ``{message}``；
    拉取成员关系的过滤规则也与服务端一致（我自己的成员行含已删除的 + 我所在账本里其他人的生效成员行）。

实现的接口：成员相关的全部接口，外加两组「刚好够用」的接口——
  - 账单导入（微信 / 支付宝的预览与正式导入，对照 ImportController + CsvParserService）：解析规则、校验顺序、状态码与消息一致，
    正式导入会记录（``cloud.imports()`` / ``cloud.imported_bills()``）；调用 ``cloud.use_import_catalog(...)`` 之后，
    拉取账单接口还会把导入的账单还给客户端，「导入后自动同步一次 → 账单列表看得到」就能端到端验证；
  - 同步的最小桩（导入前同步账本、导入后拉取账单走的那几个接口）：账本推送会登记账本（所有者以令牌为准，与真实服务一致），
    其余推送 / 拉取返回「没有变化」。
其它接口一律 404 并记录进 ``cloud.requests``，测试可以断言「没有意外的请求」。

用法::

    with FakeCloud() as cloud:                       # 端口被占用时抛 CloudPortBusy
        me = cloud.register_user(phone)              # 云端用户 Id（与本地用户 Id 不同，复现真实的 Id 映射）
        cloud.add_ledger(ledger_id, owner_phone=phone, name="日常")
        mid = cloud.add_member(ledger_id, other_phone, role=ROLE_EDITOR)
        ...
        req = cloud.wait_request("POST", r"/members$")   # 页面操作后云端确实收到了请求
        assert cloud.member(mid)["role"] == ROLE_VIEWER  # 云端状态已变化

  - ``cloud.fail(method, path_regex, status=403, message=...)``：让下一个匹配的请求按指定状态码失败（默认只一次）。
  - 本地库要与云端状态一致（Web.Server 先读本地缓存）：用 ``CloudWorld`` 同时写两边，见 e2e_base 的说明。

只依赖标准库，不依赖 playwright；协议本身由 e2e_base_test.py 自测。
"""
from __future__ import annotations

import base64
import json
import os
import re
import socket
import threading
import time
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from decimal import Decimal, InvalidOperation
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Optional

#: 假云端端口。必须与 server.ps1 的 -CloudPort 一致（它据此设置 Web.Server 的 ApiSettings__BaseUrl）
CLOUD_PORT = int(os.environ.get("E2E_CLOUD_PORT", "5271"))

# 枚举取值与 FinanceTracker.Core.Enums 保持一致
ROLE_OWNER, ROLE_EDITOR, ROLE_VIEWER = 0, 1, 2
MEMBER_PENDING, MEMBER_ACTIVE = 0, 1


class CloudPortBusy(RuntimeError):
    """假云端的端口已被占用（例如开发者本机的真实 Api 就监听在这个端口）。"""


class ApiError(Exception):
    """接口错误：对应真实控制器里 catch 后返回的 `{message}` + 状态码。"""

    def __init__(self, status: int, message: str) -> None:
        super().__init__(message)
        self.status = status
        self.message = message


@dataclass
class RecordedRequest:
    method: str
    path: str
    body: Optional[dict]
    #: 令牌里的手机号（未带令牌 / 登录请求为 None）
    phone: Optional[str]
    #: 实际返回的状态码
    status: int = 0

    def __repr__(self) -> str:  # 排错时一眼看清
        return f"{self.method} {self.path} body={self.body} phone={self.phone} -> {self.status}"


@dataclass
class _Fault:
    method: str
    path: re.Pattern
    status: int
    message: str
    times: int


@dataclass
class _Delay:
    method: str
    path: re.Pattern
    seconds: float
    times: int


def _now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f") + "Z"


def _lower(value: Optional[str]) -> Optional[str]:
    return value.lower() if value else value


def _b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


def _b64url_decode(text: str) -> bytes:
    return base64.urlsafe_b64decode(text + "=" * (-len(text) % 4))


def make_token(user_id: str, phone: str) -> str:
    """客户端只解码令牌载荷（JwtClaimReader），不校验签名：造一枚带 nameid / phone_number 的令牌即可。"""
    header = _b64url(json.dumps({"alg": "none", "typ": "JWT"}).encode())
    payload = _b64url(json.dumps({"nameid": user_id, "phone_number": phone}).encode())
    return f"{header}.{payload}.fake"


def validate_phone(phone: Optional[str]) -> Optional[str]:
    """与 FinanceTracker.Shared.Validators.PhoneValidator 一致；合法返回 None。"""
    if not phone or not phone.strip():
        return "手机号不能为空"
    if len(phone) != 11:
        return "手机号必须是11位"
    if not phone.startswith("1"):
        return "手机号格式不正确"
    return None


# ---------------------------------------------------------------------------------------------
# 账单导入：CSV 解析。对照 FinanceTracker.Core.Services.CsvParserService 的现行规则，
# 输出字段与 ImportedBill 的 JSON 一致（camelCase）。微信按固定列位置取列；支付宝按表头名称定位各列
# （真实导出的列布局：交易时间,交易分类,交易对方,对方账号,商品说明,收/支,金额,收/付款方式,交易状态,交易订单号,商家订单号,备注，
# 新旧版导出文件的叫法都认）。这里的解析只是镜像，真实导出文件（编码、表头前的说明行、字段末尾的制表符）仍要人工验收。
# ---------------------------------------------------------------------------------------------

#: 同步的最小桩：这些推送接口一律回「没有结果」，拉取接口一律回「没有变化」
#: （账本推送另有实现，见 _push_ledgers；拉取账单另有实现，见 _pull_bills）
_SYNC_EMPTY_PUSH = frozenset({"/api/sync/push", "/api/sync/categories/push", "/api/sync/paymentchannels/push"})
_SYNC_EMPTY_PULL = {
    "/api/sync/categories/pull": "categories",
    "/api/sync/paymentchannels/pull": "paymentChannels",
    "/api/sync/ledgers/pull": "ledgers",
}

#: 与 ImportController.IsValidUtcOffset 一致
UTC_OFFSET_RANGE = (-840, 840)
INVALID_UTC_OFFSET_MESSAGE = "时区偏移必须在 -840 到 840 分钟之间"
_EMPTY_GUID = "00000000-0000-0000-0000-000000000000"
_WECHAT_INVALID_STATUS = ("已撤销", "已失效", "已退返", "交易关闭", "已全额退款", "失败")


def split_csv_line(line: str) -> list[str]:
    """与 CsvParserService.ParseCsvLine 一致：引号内的逗号不分隔，引号本身丢弃，每个字段去首尾空白。"""
    fields: list[str] = []
    current: list[str] = []
    in_quotes = False
    for ch in line:
        if ch == '"':
            in_quotes = not in_quotes
        elif ch == "," and not in_quotes:
            fields.append("".join(current).strip())
            current = []
        else:
            current.append(ch)
    fields.append("".join(current).strip())
    return fields


def _placeholder(value: Optional[str]) -> bool:
    """与 CsvParserService.IsPlaceholder 一致：微信与支付宝导出文件都用「/」或空白表示没有。"""
    return value is None or not value.strip() or value.strip() == "/"


def _wechat_description(f: list[str]) -> str:
    product = f[3] if len(f) > 3 else ""
    if not _placeholder(product):
        return product
    remark = f[10] if len(f) > 10 else ""
    if not _placeholder(remark):
        return remark
    kind = f[1] if len(f) > 1 else ""
    if not _placeholder(kind):
        return kind
    counterparty = f[2] if len(f) > 2 else ""
    return "微信交易" if _placeholder(counterparty) else counterparty


def _wechat_merchant(f: list[str]) -> str:
    counterparty = f[2] if len(f) > 2 else ""
    if not _placeholder(counterparty):
        return counterparty
    kind = f[1] if len(f) > 1 else ""
    return "微信交易" if _placeholder(kind) else kind


def _wechat_channel(method: str) -> str:
    if _placeholder(method):
        return "微信支付"
    if "信用卡" in method:
        return "信用卡"
    if "储蓄卡" in method or "借记卡" in method or "银行卡" in method:
        return "银行卡"
    return "微信支付"


def _parse_bill_time(text: str) -> datetime:
    text = text.strip()
    for fmt in ("%Y/%m/%d %H:%M", "%Y-%m-%d %H:%M:%S", "%Y-%m-%d %H:%M", "%Y/%m/%d %H:%M:%S", "%Y-%m-%d", "%Y/%m/%d"):
        try:
            return datetime.strptime(text, fmt)
        except ValueError:
            continue
    raise ValueError(f"无法解析日期: {text}")


def _parse_amount(text: str) -> Decimal:
    cleaned = text.replace("¥", "").replace("￥", "").replace(" ", "").strip().replace(",", "")
    try:
        return abs(Decimal(cleaned))
    except InvalidOperation as exc:
        raise ValueError(f"无法解析金额: {cleaned}") from exc


def _wall_text(moment: datetime) -> str:
    """ImportedBill.TransactionTime 的 JSON 形态：Kind 为 Unspecified 的 DateTime 没有时区后缀。"""
    return moment.strftime("%Y-%m-%dT%H:%M:%S")


def _csv_lines(text: str) -> list[str]:
    # 与 C# 的 Split('\n', RemoveEmptyEntries) 一致（不去空白，\r 之类留给逐行的 Trim）
    return [line for line in text.split("\n") if line != ""]


def parse_wechat_csv(text: str) -> "tuple[list[dict], int]":
    """微信账单：返回 (账单列表, 跳过笔数)。收/支不是「收入 / 支出」（中性交易）、状态无效、金额 ≤ 0 的行计入跳过；
    日期 / 金额解析失败的行与 C# 一样静默丢弃（不计入跳过）。"""
    lines = _csv_lines(text)
    start = next((i for i, line in enumerate(lines) if "交易时间" in line and "交易单号" in line), -1)
    if start < 0:
        return [], 0
    bills: list[dict] = []
    skipped = 0
    for raw in lines[start + 1:]:
        line = raw.strip()
        if not line:
            continue
        try:
            f = split_csv_line(line)
            if len(f) < 6:
                continue
            direction = f[4].strip()
            if direction not in ("收入", "支出"):
                skipped += 1
                continue
            status = f[7] if len(f) > 7 else ""
            if not _placeholder(status) and any(word in status for word in _WECHAT_INVALID_STATUS):
                skipped += 1
                continue
            moment = _parse_bill_time(f[0])
            amount = _parse_amount(f[5])
            if amount <= 0:
                skipped += 1
                continue
            bills.append({
                "transactionTime": _wall_text(moment),
                "description": _wechat_description(f),
                "merchantName": _wechat_merchant(f),
                "amount": float(amount),
                "isIncome": direction == "收入",
                "paymentChannel": _wechat_channel(f[6] if len(f) > 6 else ""),
                "transactionId": f[8] if len(f) > 8 else "",
            })
        except ValueError:
            continue
    return bills, skipped


_ALIPAY_INVALID_STATUS = ("交易关闭", "已关闭", "已撤销", "已失效", "已全额退款", "失败")


def _alipay_columns(header: list[str]) -> Optional[dict]:
    """与 CsvParserService.AlipayColumns 一致：按表头名称定位各列（找不到的列是 -1，新旧版导出的叫法都认）；
    必需的三列（时间 / 收支 / 金额）缺一个就返回 None；表头里有微信才有的「当前状态」列（选错来源把微信账单当支付宝）也返回 None。"""
    names = [h.strip("\ufeff \t\r").replace("（", "(").replace("）", ")") for h in header]

    def find(*candidates: str) -> int:
        for candidate in candidates:
            if candidate in names:
                return names.index(candidate)
        return -1

    columns = {
        "time": find("交易时间", "交易创建时间", "付款时间"),
        "category": find("交易分类"),
        "counterparty": find("交易对方"),
        "product": find("商品说明", "商品名称"),
        "direction": find("收/支"),
        "amount": find("金额", "金额(元)"),
        "status": find("交易状态"),
        "txid": find("交易订单号", "交易号"),
    }
    if "当前状态" in names:
        return None
    return columns if min(columns["time"], columns["direction"], columns["amount"]) >= 0 else None


def parse_alipay_csv(text: str) -> list[dict]:
    """支付宝账单：与 CsvParserService.ParseAlipayCsvAsync 一致——各列按表头名称定位（不按固定位置），
    只收「收/支」是收入 / 支出（「不计收支」不计入）、交易状态有效（交易关闭等不计入）、金额 > 0 的行；
    不返回跳过笔数（真实接口也没有）；日期 / 金额解析失败的行静默丢弃。"""
    lines = _csv_lines(text)
    start = next((i for i, line in enumerate(lines) if ("交易时间" in line or "交易创建时间" in line) and "金额" in line), -1)
    if start < 0:
        return []
    columns = _alipay_columns(split_csv_line(lines[start].strip()))
    if columns is None:
        return []
    required_max = max(columns["time"], columns["direction"], columns["amount"])

    def get(fields: list[str], key: str) -> str:
        index = columns[key]
        return fields[index] if 0 <= index < len(fields) else ""

    def description(fields: list[str]) -> str:
        for key in ("product", "category"):
            if not _placeholder(get(fields, key)):
                return get(fields, key)
        counterparty = get(fields, "counterparty")
        return "支付宝交易" if _placeholder(counterparty) else counterparty

    def merchant(fields: list[str]) -> str:
        counterparty = get(fields, "counterparty")
        if not _placeholder(counterparty):
            return counterparty
        category = get(fields, "category")
        return "支付宝交易" if _placeholder(category) else category

    bills: list[dict] = []
    for raw in lines[start + 1:]:
        line = raw.strip()
        if not line:
            continue
        try:
            f = split_csv_line(line)
            if len(f) <= required_max:
                continue
            direction = get(f, "direction")
            if direction not in ("收入", "支出"):
                continue
            status = get(f, "status")
            if not _placeholder(status) and any(word in status for word in _ALIPAY_INVALID_STATUS):
                continue
            amount = _parse_amount(get(f, "amount"))
            if amount <= 0:
                continue
            moment = _parse_bill_time(get(f, "time"))
            bills.append({
                "transactionTime": _wall_text(moment),
                "description": description(f),
                "merchantName": merchant(f),
                "amount": float(amount),
                "isIncome": direction == "收入",
                "paymentChannel": "支付宝",
                "transactionId": get(f, "txid"),
            })
        except (ValueError, InvalidOperation):
            continue
    return bills


class FakeCloud:
    """进程内的假云端（线程里跑一个 HTTP 服务；状态用锁保护）。"""

    def __init__(self, port: int = CLOUD_PORT) -> None:
        self.port = port
        # 与 server.ps1 给 Web.Server 的云端地址一致，写 127.0.0.1 而不是 localhost（见该脚本头部：避开 ::1 被拒绝的 2 秒延迟）
        self.url = f"http://127.0.0.1:{port}"
        self.requests: list[RecordedRequest] = []
        self._lock = threading.RLock()
        self._users: dict[str, dict] = {}  # phone -> {id, phone}
        self._ledgers: dict[str, dict] = {}  # ledger id -> {id, name, icon, ownerId, isDeleted}
        self._members: dict[str, dict] = {}  # member id -> 成员行（字段同 LedgerMemberSyncDto 的核心部分）
        self._imports: list[dict] = []  # 收下的正式导入（预览不算）
        self._import_row_failures: dict[int, str] = {}  # 下一次正式导入里「入库失败」的账单序号（从 1 起）→ 原因
        self._import_catalog: Optional[dict] = None  # 设了之后，拉取账单接口会把导入的账单还给客户端（见 use_import_catalog）
        self._faults: list[_Fault] = []
        self._delays: list[_Delay] = []
        self._server: Optional[ThreadingHTTPServer] = None
        self._thread: Optional[threading.Thread] = None

    # ------------------------------------------------------------ 生命周期

    def start(self) -> "FakeCloud":
        cloud = self

        class Handler(BaseHTTPRequestHandler):
            # HTTP/1.0：每个响应后关闭连接，Web.Server 的连接池里不会残留指向已停止假云端的陈旧连接
            protocol_version = "HTTP/1.0"

            def log_message(self, *_args: Any) -> None:  # 静默
                pass

            def _handle(self) -> None:
                length = int(self.headers.get("Content-Length") or 0)
                raw = self.rfile.read(length) if length else b""
                status, body = cloud._dispatch(self.command, self.path, raw, self.headers.get("Authorization"))
                payload = b"" if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8")
                self.send_response(status)
                if payload:
                    self.send_header("Content-Type", "application/json; charset=utf-8")
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                if payload and self.command != "HEAD":
                    self.wfile.write(payload)

            do_GET = do_POST = do_PUT = do_DELETE = _handle

        class _Server(ThreadingHTTPServer):
            # Windows 上 SO_REUSEADDR 允许与别的进程同时绑定同一个端口（连接会被随机分流），端口被占用时必须报错而不是悄悄共用
            allow_reuse_address = False
            daemon_threads = True

        try:
            # 只监听本机回环（IPv4）：这是个没有鉴权的内存假实现，不能暴露到局域网。
            # 所以 Web.Server 必须用 127.0.0.1 访问它：用 localhost 时 .NET 先连 ::1，被拒绝的回环连接在 Windows 11 上要
            # 2 秒才返回（实测），每个云端请求白白多 2 秒
            self._server = _Server(("127.0.0.1", self.port), Handler)
        except OSError as exc:
            raise CloudPortBusy(f"假云端端口 {self.port} 已被占用：{exc}") from exc
        self._thread = threading.Thread(target=self._server.serve_forever, kwargs={"poll_interval": 0.05}, daemon=True)
        self._thread.start()
        self._wait_listening()
        return self

    def stop(self) -> None:
        if self._server is not None:
            self._server.shutdown()
            self._server.server_close()
            self._server = None
        if self._thread is not None:
            self._thread.join(timeout=5)
            self._thread = None

    def __enter__(self) -> "FakeCloud":
        return self.start()

    def __exit__(self, *_exc: Any) -> None:
        self.stop()

    def _wait_listening(self) -> None:
        deadline = time.time() + 5
        while time.time() < deadline:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
                probe.settimeout(0.2)
                if probe.connect_ex(("127.0.0.1", self.port)) == 0:
                    return
            time.sleep(0.05)
        raise RuntimeError(f"假云端启动后端口 {self.port} 一直连不上")

    # ------------------------------------------------------------ 造数据 / 读状态

    def register_user(self, phone: str, user_id: Optional[str] = None) -> str:
        """注册（或取回）云端用户，返回云端用户 Id（小写 Guid 文本）。

        登录用户（测试里 login() 的那个）不指定 user_id：云端 Id 与本地用户 Id 各自随机，复现生产里的 Id 映射；
        「其他人」指定成本地造用户时用的同一个 Id，这样两边引用同一个人。
        """
        with self._lock:
            user = self._users.get(phone)
            if user is None:
                user = {"id": _lower(user_id) or str(uuid.uuid4()), "phone": phone}
                self._users[phone] = user
            return user["id"]

    def add_ledger(self, ledger_id: str, owner_phone: str, name: str, icon: str = "mdi-book") -> str:
        """登记账本（Id 用本地账本的 Id），同时写入所有者的 Owner 成员行，返回该成员行 Id。"""
        owner_id = self.register_user(owner_phone)
        with self._lock:
            self._ledgers[_lower(ledger_id)] = {
                "id": _lower(ledger_id), "name": name, "icon": icon, "ownerId": owner_id, "isDeleted": False,
            }
        return self.add_member(ledger_id, owner_phone, role=ROLE_OWNER, status=MEMBER_ACTIVE)

    def add_member(
        self,
        ledger_id: str,
        phone: str,
        *,
        role: int = ROLE_EDITOR,
        status: int = MEMBER_ACTIVE,
        member_id: Optional[str] = None,
    ) -> str:
        """登记成员关系（用户不存在会先注册）。返回成员行 Id（小写）。"""
        user_id = self.register_user(phone)
        mid = _lower(member_id) or str(uuid.uuid4())
        now = _now()
        with self._lock:
            self._members[mid] = {
                "id": mid, "ledgerId": _lower(ledger_id), "userId": user_id, "role": role, "status": status,
                "isDeleted": False, "createdAt": now, "updatedAt": now,
            }
        return mid

    def member(self, member_id: str) -> dict:
        with self._lock:
            return dict(self._members[_lower(member_id)])

    def members_of(self, ledger_id: str, *, include_deleted: bool = False) -> list[dict]:
        with self._lock:
            rows = [
                dict(m) for m in self._members.values()
                if m["ledgerId"] == _lower(ledger_id) and (include_deleted or not m["isDeleted"])
            ]
        return sorted(rows, key=lambda m: (m["role"], m["createdAt"]))

    def member_of_phone(self, ledger_id: str, phone: str) -> Optional[dict]:
        """某个手机号在该账本里的成员行（取最近一条，含已删除的）。"""
        with self._lock:
            user = self._users.get(phone)
            if user is None:
                return None
            rows = [m for m in self._members.values() if m["ledgerId"] == _lower(ledger_id) and m["userId"] == user["id"]]
        return dict(sorted(rows, key=lambda m: m["updatedAt"])[-1]) if rows else None

    def ledger(self, ledger_id: str) -> dict:
        with self._lock:
            return dict(self._ledgers[_lower(ledger_id)])

    def user_id(self, phone: str) -> str:
        with self._lock:
            return self._users[phone]["id"]

    # ------------------------------------------------------------ 观察 / 故障注入

    def calls(self, method: Optional[str] = None, path_regex: str = "") -> list[RecordedRequest]:
        pattern = re.compile(path_regex)
        with self._lock:
            return [r for r in self.requests if (method is None or r.method == method) and pattern.search(r.path)]

    def wait_request(
        self, method: Optional[str], path_regex: str, *, timeout_s: float = 10.0, count: int = 1
    ) -> Optional[RecordedRequest]:
        """等到收到第 count 个匹配的请求；超时返回 None（调用方据此报「Web.Server 没有访问假云端」）。"""
        deadline = time.time() + timeout_s
        while time.time() < deadline:
            found = self.calls(method, path_regex)
            if len(found) >= count:
                return found[count - 1]
            time.sleep(0.05)
        return None

    def fail(self, method: str, path_regex: str, *, status: int = 500, message: str = "故障注入", times: int = 1) -> None:
        """让接下来 times 个匹配的请求直接以 status 失败（不触碰状态）。"""
        with self._lock:
            self._faults.append(_Fault(method.upper(), re.compile(path_regex), status, message, times))

    def clear_faults(self) -> None:
        with self._lock:
            self._faults.clear()

    def delay(self, method: str, path_regex: str, seconds: float, *, times: int = 1) -> None:
        """让接下来 times 个匹配的请求「慢」seconds 秒才处理（请求先记录、到点才处理并响应），
        用来观察页面的「解析中 / 导入中」这类转瞬即逝的中间状态。睡在锁外，不会卡住别的请求与测试自己读假云端。"""
        with self._lock:
            self._delays.append(_Delay(method.upper(), re.compile(path_regex), seconds, times))

    # ------------------------------------------------------------ 账单导入：观察 / 故障注入

    def imports(self, source: Optional[str] = None) -> list[dict]:
        """云端收下的正式导入（预览不算，校验没通过的请求也不算），按到达顺序。

        每项：source（wechat / alipay）、ledgerId、userId（云端用户 Id）、utcOffsetMinutes（客户端上报的，可能为 None）、
        csv（收到的 CSV 原文）、bills（入库成功的账单，每条带 ledgerId / userId / transactionTimeUtc——按上报的偏移把 CSV
        里的本地时间换算成 UTC）、errors（入库失败的原因，含「第 N 条: …」前缀）。
        """
        with self._lock:
            return [dict(item) for item in self._imports if source is None or item["source"] == source]

    def imported_bills(self, ledger_id: Optional[str] = None) -> list[dict]:
        """所有入库成功的账单（可按账本过滤）。"""
        wanted = _lower(ledger_id)
        with self._lock:
            return [dict(b) for item in self._imports for b in item["bills"] if wanted is None or b["ledgerId"] == wanted]

    def fail_import_rows(self, rows: "dict[int, str]") -> None:
        """让下一次正式导入里指定序号（从 1 起）的账单入库失败，原因记入 errors（格式同 ProcessImportedBills：「第 N 条: 原因」）。
        只生效一次（下一次正式导入用掉它）。"""
        with self._lock:
            self._import_row_failures = dict(rows)

    def use_import_catalog(self, *, expense_category_id: str, income_category_id: str, channel_id: str) -> None:
        """让导入的账单能被「拉取账单」接口还给客户端（验证「导入后自动同步一次 → 本地账单列表看得到」）。

        真实服务按关键字给导入的账单挑分类 / 支付渠道（都是预设，Id 在各端一致）；假云端不做关键字匹配，
        统一用测试给的这三个 Id（用本地库里的预设 Id，客户端合并账单时外键才对得上）。没调用这个方法时，
        拉取账单接口返回「没有账单」。"""
        with self._lock:
            self._import_catalog = {
                "expense": _lower(expense_category_id),
                "income": _lower(income_category_id),
                "channel": _lower(channel_id),
            }

    # ------------------------------------------------------------ 请求分发

    def _dispatch(self, method: str, raw_path: str, raw_body: bytes, authorization: Optional[str]) -> "tuple[int, Any]":
        path = raw_path.split("?", 1)[0]
        body: Optional[dict] = None
        if raw_body:
            try:
                parsed = json.loads(raw_body.decode("utf-8"))
                body = parsed if isinstance(parsed, dict) else {"_": parsed}
            except ValueError:
                body = None

        with self._lock:
            record = RecordedRequest(method, path, body, None)
            self.requests.append(record)
            pause = 0.0
            for slow in list(self._delays):
                if slow.method == method and slow.path.search(path) and slow.times > 0:
                    slow.times -= 1
                    if slow.times == 0:
                        self._delays.remove(slow)
                    pause = slow.seconds
                    break
        if pause:
            time.sleep(pause)

        with self._lock:
            try:
                for fault in list(self._faults):
                    if fault.method == method and fault.path.search(path) and fault.times > 0:
                        fault.times -= 1
                        if fault.times == 0:
                            self._faults.remove(fault)
                        raise ApiError(fault.status, fault.message)

                status, result = self._route(method, path, body, authorization, record)
            except ApiError as err:
                status, result = err.status, {"message": err.message}
            record.status = status
            return status, result

    @staticmethod
    def _keys(body: Optional[dict]) -> dict:
        """请求体的键不区分大小写（客户端用 PascalCase 序列化，真实接口大小写不敏感）。"""
        return {k.lower(): v for k, v in (body or {}).items()}

    def _auth(self, authorization: Optional[str], record: RecordedRequest) -> dict:
        if not authorization or not authorization.lower().startswith("bearer "):
            raise ApiError(401, "未授权")
        token = authorization.split(" ", 1)[1].strip()
        try:
            claims = json.loads(_b64url_decode(token.split(".")[1]))
        except (IndexError, ValueError):
            raise ApiError(401, "令牌无效")
        user_id = claims.get("nameid")
        user = next((u for u in self._users.values() if u["id"] == user_id), None)
        if user is None:
            raise ApiError(401, "令牌无效")
        record.phone = user["phone"]
        return user

    def _route(self, method: str, path: str, body: Optional[dict], authorization: Optional[str], record: RecordedRequest):
        if method == "POST" and path == "/api/auth/login":
            return self._login(body)

        # 以下接口都需要令牌
        me = self._auth(authorization, record)
        keys = self._keys(body)

        if method == "POST" and path == "/api/sync/ledgermembers/pull":
            return 200, {"members": [self._dto(m) for m in self._pull_for(me["id"], keys.get("since"))]}

        found = re.fullmatch(r"/api/ledgers/([0-9a-fA-F-]{36})/members", path)
        if method == "POST" and found:
            return 200, self._dto(self._invite(me, found.group(1).lower(), keys))

        found = re.fullmatch(r"/api/ledgers/([0-9a-fA-F-]{36})/members/([0-9a-fA-F-]{36})/role", path)
        if method == "PUT" and found:
            self._change_role(me, found.group(1).lower(), found.group(2).lower(), keys)
            return 204, None

        found = re.fullmatch(r"/api/ledgers/([0-9a-fA-F-]{36})/members/([0-9a-fA-F-]{36})", path)
        if method == "DELETE" and found:
            self._remove(me, found.group(1).lower(), found.group(2).lower())
            return 204, None

        found = re.fullmatch(r"/api/ledgers/([0-9a-fA-F-]{36})/transfer-ownership", path)
        if method == "POST" and found:
            self._transfer(me, found.group(1).lower(), keys)
            return 204, None

        found = re.fullmatch(r"/api/ledgers/([0-9a-fA-F-]{36})/exit", path)
        if method == "POST" and found:
            self._exit(me, found.group(1).lower())
            return 204, None

        found = re.fullmatch(r"/api/invitations/([0-9a-fA-F-]{36})/respond", path)
        if method == "POST" and found:
            self._respond(me, found.group(1).lower(), keys)
            return 204, None

        # ---- 账单导入（20 号）
        found = re.fullmatch(r"/api/import/(wechat|alipay)(/preview)?", path)
        if method == "POST" and found:
            if found.group(2):
                return 200, self._preview(found.group(1), keys)
            return 200, self._import(found.group(1), me, keys)

        # ---- 同步的最小桩（20 号）：账本推送会登记账本，其余推送 / 拉取都是「没有变化」
        if method == "POST" and path == "/api/sync/ledgers/push":
            return 200, {"results": self._push_ledgers(me, keys)}
        if method == "POST" and path in _SYNC_EMPTY_PUSH:
            return 200, {"results": []}
        if method == "POST" and path == "/api/sync/pull":
            return 200, {"bills": self._pull_bills(me)}
        if method == "POST" and path in _SYNC_EMPTY_PULL:
            return 200, {_SYNC_EMPTY_PULL[path]: []}

        return 404, {"message": f"假云端没有实现 {method} {path}"}

    # ------------------------------------------------------------ 接口实现（对照真实服务的消息与状态码）

    def _login(self, body: Optional[dict]):
        phone = self._keys(body).get("phonenumber")
        problem = validate_phone(phone)
        if problem:
            raise ApiError(400, problem)
        user_id = self.register_user(phone)
        return 200, {"userId": user_id, "phoneNumber": phone, "token": make_token(user_id, phone)}

    def _pull_for(self, user_id: str, since: Optional[str]) -> list[dict]:
        """与 SyncController.PullLedgerMembers 一致：我的成员行（含已删除 / 待确认）+ 我所在账本里其他人的生效成员行。"""
        my_ledgers = {m["ledgerId"] for m in self._members.values() if m["userId"] == user_id and not m["isDeleted"]}
        rows = [
            m for m in self._members.values()
            if m["userId"] == user_id
            or (m["status"] == MEMBER_ACTIVE and not m["isDeleted"] and m["ledgerId"] in my_ledgers)
        ]
        if since:
            rows = [m for m in rows if m["updatedAt"] >= since]
        return sorted(rows, key=lambda m: m["updatedAt"])

    def _dto(self, member: dict) -> dict:
        user = next((u for u in self._users.values() if u["id"] == member["userId"]), None)
        ledger = self._ledgers.get(member["ledgerId"])
        return {
            **member,
            "userPhoneNumber": user["phone"] if user else None,
            "ledgerName": ledger["name"] if ledger else None,
            "ledgerIcon": ledger["icon"] if ledger else None,
            "ledgerOwnerId": ledger["ownerId"] if ledger else None,
        }

    def _ledger(self, ledger_id: str) -> dict:
        ledger = self._ledgers.get(ledger_id)
        if ledger is None or ledger["isDeleted"]:
            raise ApiError(404, "账本不存在")
        return ledger

    def _require_owner(self, me: dict, ledger_id: str) -> dict:
        ledger = self._ledger(ledger_id)
        if ledger["ownerId"] != me["id"]:
            raise ApiError(403, "只有账本所有者可以执行此操作")
        return ledger

    def _touch(self, member: dict) -> None:
        member["updatedAt"] = _now()

    def _invite(self, me: dict, ledger_id: str, keys: dict) -> dict:
        phone = (keys.get("phonenumber") or "").strip()
        if not phone:
            raise ApiError(400, "手机号不能为空")
        role = keys.get("role")
        if role == ROLE_OWNER:
            raise ApiError(409, "不能邀请 Owner 角色，请使用转让所有权")
        ledger = self._ledgers.get(ledger_id)
        if ledger is None or ledger["isDeleted"]:
            raise ApiError(400, "账本不存在")
        my_row = next(
            (m for m in self._members.values()
             if m["ledgerId"] == ledger_id and m["userId"] == me["id"] and m["status"] == MEMBER_ACTIVE and not m["isDeleted"]),
            None,
        )
        if my_row is None or my_row["role"] != ROLE_OWNER:
            raise ApiError(403, "只有账本所有者可以邀请成员")
        invited = self._users.get(phone)
        if invited is None:
            raise ApiError(400, "该手机号尚未注册")
        if invited["id"] == ledger["ownerId"]:
            raise ApiError(409, "该用户是账本所有者")
        existing = next((m for m in self._members.values() if m["ledgerId"] == ledger_id and m["userId"] == invited["id"]), None)
        if existing is not None:
            if not existing["isDeleted"]:
                raise ApiError(409, "该用户已被邀请，等待确认" if existing["status"] == MEMBER_PENDING else "该用户已是账本成员")
            existing.update(role=role, status=MEMBER_PENDING, isDeleted=False)
            self._touch(existing)
            return existing
        mid = str(uuid.uuid4())
        now = _now()
        member = {
            "id": mid, "ledgerId": ledger_id, "userId": invited["id"], "role": role, "status": MEMBER_PENDING,
            "isDeleted": False, "createdAt": now, "updatedAt": now,
        }
        self._members[mid] = member
        return member

    def _change_role(self, me: dict, ledger_id: str, member_id: str, keys: dict) -> None:
        self._require_owner(me, ledger_id)
        new_role = keys.get("role")
        if new_role == ROLE_OWNER:
            raise ApiError(400, "请使用转让所有权变更 Owner")
        member = self._members.get(member_id)
        if (member is None or member["ledgerId"] != ledger_id
                or member["status"] != MEMBER_ACTIVE or member["isDeleted"]):
            raise ApiError(404, "成员不存在")
        if member["role"] == ROLE_OWNER:
            raise ApiError(400, "不能修改账本所有者的角色")
        member["role"] = new_role
        self._touch(member)

    def _remove(self, me: dict, ledger_id: str, member_id: str) -> None:
        self._require_owner(me, ledger_id)
        member = self._members.get(member_id)
        if member is None or member["ledgerId"] != ledger_id or member["isDeleted"]:
            raise ApiError(404, "成员不存在")
        if member["role"] == ROLE_OWNER:
            raise ApiError(400, "不能移除账本所有者，请先转让所有权")
        member["isDeleted"] = True
        self._touch(member)

    def _transfer(self, me: dict, ledger_id: str, keys: dict) -> None:
        ledger = self._require_owner(me, ledger_id)
        to_user = keys.get("touserid")
        if to_user and to_user.lower() == me["id"]:
            raise ApiError(400, "不能转让给自己")
        target = next(
            (m for m in self._members.values()
             if m["ledgerId"] == ledger_id and m["userId"] == (to_user or "").lower()
             and m["status"] == MEMBER_ACTIVE and not m["isDeleted"]),
            None,
        )
        if target is None:
            raise ApiError(404, "目标用户不是该账本的生效成员")
        owner_row = next(m for m in self._members.values() if m["ledgerId"] == ledger_id and m["userId"] == me["id"])
        ledger["ownerId"] = target["userId"]
        owner_row["role"] = ROLE_EDITOR
        target["role"] = ROLE_OWNER
        self._touch(owner_row)
        self._touch(target)

    def _exit(self, me: dict, ledger_id: str) -> None:
        ledger = self._ledger(ledger_id)
        if ledger["ownerId"] == me["id"]:
            raise ApiError(400, "账本所有者不能退出账本，请先转让所有权")
        member = next(
            (m for m in self._members.values()
             if m["ledgerId"] == ledger_id and m["userId"] == me["id"] and m["status"] == MEMBER_ACTIVE and not m["isDeleted"]),
            None,
        )
        if member is None:
            raise ApiError(404, "你不是该账本成员")
        member["isDeleted"] = True
        self._touch(member)

    def _respond(self, me: dict, member_id: str, keys: dict) -> None:
        member = self._members.get(member_id)
        if member is None or member["isDeleted"] or member["userId"] != me["id"] or member["status"] != MEMBER_PENDING:
            raise ApiError(404, "邀请不存在或已处理")
        ledger = self._ledgers.get(member["ledgerId"])
        if ledger is None or ledger["isDeleted"]:
            raise ApiError(400, "该账本已被删除，邀请失效")
        if keys.get("accept"):
            member["status"] = MEMBER_ACTIVE
        else:
            member["isDeleted"] = True
        self._touch(member)

    # ------------------------------------------------------------ 账单导入 / 同步桩（对照 ImportController、SyncController）

    @staticmethod
    def _csv_of(keys: dict) -> str:
        text = keys.get("csvcontent")
        if not text or not str(text).strip():
            raise ApiError(400, "CSV 内容不能为空")
        return str(text)

    def _preview(self, source: str, keys: dict) -> dict:
        """预览：只解析不入库，不要求账本。微信带 skippedCount，支付宝没有（与真实接口一致）。"""
        csv_text = self._csv_of(keys)
        if source == "wechat":
            bills, skipped = parse_wechat_csv(csv_text)
            return {"totalCount": len(bills), "skippedCount": skipped, "bills": bills}
        bills = parse_alipay_csv(csv_text)
        return {"totalCount": len(bills), "bills": bills}

    def _check_import_ledger(self, me: dict, ledger_value: Any) -> str:
        """账本必填 + 写权限（ImportController.ValidateImportLedgerAsync → BaseApiController.CheckLedgerWritableAsync）。"""
        ledger_id = str(ledger_value or "").lower()
        if not ledger_id or ledger_id == _EMPTY_GUID:
            raise ApiError(400, "请选择要导入到的账本")
        ledger = self._ledgers.get(ledger_id)
        if ledger is None or ledger["isDeleted"]:
            raise ApiError(400, "账本不存在")
        if ledger["ownerId"] == me["id"]:
            return ledger_id
        row = next(
            (m for m in self._members.values()
             if m["ledgerId"] == ledger_id and m["userId"] == me["id"] and m["status"] == MEMBER_ACTIVE and not m["isDeleted"]),
            None,
        )
        if row is None:
            raise ApiError(403, "无权向该账本记账")
        if row["role"] == ROLE_VIEWER:
            raise ApiError(403, "只读成员不能在共享账本中记账")
        return ledger_id

    def _import(self, source: str, me: dict, keys: dict) -> dict:
        """正式导入。校验顺序与真实接口一致：CSV 非空 → 时区偏移范围 → 账本必填与写权限 → 解析入库。"""
        csv_text = self._csv_of(keys)
        offset = keys.get("utcoffsetminutes")
        if offset is not None and not (UTC_OFFSET_RANGE[0] <= offset <= UTC_OFFSET_RANGE[1]):
            raise ApiError(400, INVALID_UTC_OFFSET_MESSAGE)
        ledger_id = self._check_import_ledger(me, keys.get("ledgerid"))

        bills = parse_wechat_csv(csv_text)[0] if source == "wechat" else parse_alipay_csv(csv_text)
        failures, self._import_row_failures = self._import_row_failures, {}
        stored: list[dict] = []
        errors: list[str] = []
        for index, bill in enumerate(bills, start=1):
            if index in failures:
                errors.append(f"第 {index} 条: {failures[index]}")
                continue
            wall = datetime.strptime(bill["transactionTime"], "%Y-%m-%dT%H:%M:%S")
            utc = wall - timedelta(minutes=offset) if offset is not None else wall
            stored.append({
                **bill, "id": str(uuid.uuid4()), "ledgerId": ledger_id, "userId": me["id"], "createdAt": _now(),
                "transactionTimeUtc": utc.strftime("%Y-%m-%dT%H:%M:%SZ"),
            })
        self._imports.append({
            "source": source, "ledgerId": ledger_id, "userId": me["id"], "utcOffsetMinutes": offset,
            "csv": csv_text, "bills": stored, "errors": errors,
        })
        return {
            "totalCount": len(bills), "successCount": len(stored), "failCount": len(errors),
            "errors": errors[:10],  # 与真实接口一样只返回前 10 个错误
        }

    def _pull_bills(self, me: dict) -> list[dict]:
        """拉取账单（BillSyncDto 的 JSON 形态：枚举是数字，时间带 Z）。没调用 use_import_catalog 就是「没有账单」；
        设了之后只还「我导入的账单」（共享账本里别人的账单不在这个假实现的范围内）。"""
        catalog = self._import_catalog
        if catalog is None:
            return []
        result: list[dict] = []
        for item in self._imports:
            for bill in item["bills"]:
                if bill["userId"] != me["id"]:
                    continue
                ledger = self._ledgers.get(bill["ledgerId"])
                result.append({
                    "id": bill["id"],
                    "userId": bill["userId"],
                    "amount": bill["amount"],
                    "type": 1 if bill["isIncome"] else 0,
                    "categoryId": catalog["income"] if bill["isIncome"] else catalog["expense"],
                    "paymentChannelId": catalog["channel"],
                    "ledgerId": bill["ledgerId"],
                    "transactionTime": bill["transactionTimeUtc"],
                    "note": bill["description"],
                    "source": 1,  # BillSource.Import
                    "syncStatus": 1,  # SyncStatus.Synced
                    "isDeleted": False,
                    "createdAt": bill["createdAt"],
                    "updatedAt": bill["createdAt"],
                    "ledgerName": ledger["name"] if ledger else None,
                    "ledgerIcon": ledger["icon"] if ledger else None,
                })
        return result

    def _push_ledgers(self, me: dict, keys: dict) -> list[dict]:
        """账本推送：不认识的账本登记到云端（所有者以令牌为准，不信任 DTO 的 OwnerId——与真实服务一致）；
        自己的账本更新名称 / 图标 / 删除标记；别人的账本跳过。"""
        results: list[dict] = []
        for dto in keys.get("ledgers") or []:
            item = {str(k).lower(): v for k, v in dto.items()}
            ledger_id = str(item.get("id") or "").lower()
            if not ledger_id:
                continue
            existing = self._ledgers.get(ledger_id)
            if existing is not None and existing["ownerId"] != me["id"]:
                results.append({"ledgerId": ledger_id, "action": "skipped", "error": "账本属于其他用户"})
                continue
            fields = {"name": item.get("name") or "", "icon": item.get("icon") or "", "isDeleted": bool(item.get("isdeleted"))}
            if existing is None:
                self._ledgers[ledger_id] = {"id": ledger_id, "ownerId": me["id"], **fields}
            else:
                existing.update(fields)
            results.append({"ledgerId": ledger_id, "action": "pushed"})
        return results


@dataclass(frozen=True)
class Person:
    """一个用户：本地库里的 Id（大写）与云端的 Id（小写）。

    「别人」两边用同一个 Id（引用同一个人）；「我」（测试里登录的那个）两边各是各的 Id——复现生产里
    「本地与云端各自生成用户 Id、同步时映射」的情形。
    """

    phone: str
    local_id: str
    cloud_id: str


class CloudWorld:
    """同时写本地库与假云端，让两边的成员关系保持一致。

    Web.Server 先读本地缓存，成员操作才打云端，随后把云端的成员关系拉回缓存——所以造数据必须两边都写，
    并且成员行 Id、账本 Id、「别人」的用户 Id 在两边完全相同。

        world = CloudWorld(cloud, db)
        me = world.me(info.phone)                        # 已登录用户（本地库里已有）
        other = world.person()                           # 一个别的用户
        world.own_ledger(me, ledger_id, name)            # 我的账本（本地账本已存在）：云端登记 + Owner 行
        mid = world.add_member(ledger_id, other, role=ROLE_EDITOR)
        shared = world.foreign_ledger(other, "周末小组", me=me, my_role=ROLE_VIEWER)   # 别人的账本，我是只读成员
    """

    def __init__(self, cloud: FakeCloud, db: Any) -> None:
        self.cloud = cloud
        self.db = db

    def me(self, phone: str) -> Person:
        return Person(phone, self.db.user_id(phone), self.cloud.register_user(phone))

    def person(self, phone: Optional[str] = None) -> Person:
        from e2e_base import unique_phone  # 延迟导入：本模块不依赖 playwright，只有造数据时才需要基座

        phone = phone or unique_phone()
        local_id = self.db.seed_user(phone)
        return Person(phone, local_id, self.cloud.register_user(phone, user_id=local_id))

    def own_ledger(self, owner: Person, ledger_id: str, name: str, *, icon: str = "mdi-book") -> str:
        """我的账本（本地账本已经存在）：云端登记账本与 Owner 行，本地补同一条 Owner 行。返回 Owner 成员行 Id。"""
        mid = self.cloud.add_ledger(ledger_id, owner.phone, name, icon)
        self.db.seed_ledger_member(ledger_id, owner.local_id, role=ROLE_OWNER, status=MEMBER_ACTIVE, member_id=mid)
        return mid

    def add_member(self, ledger_id: str, person: Person, *, role: int = ROLE_EDITOR, status: int = MEMBER_ACTIVE) -> str:
        """成员关系：云端与本地各写一条同 Id 的行。返回成员行 Id（小写）。"""
        mid = self.cloud.add_member(ledger_id, person.phone, role=role, status=status)
        self.db.seed_ledger_member(ledger_id, person.local_id, role=role, status=status, member_id=mid)
        return mid

    def foreign_ledger(
        self,
        owner: Person,
        name: str,
        *,
        me: Person,
        my_role: int = ROLE_EDITOR,
        my_status: int = MEMBER_ACTIVE,
        icon: str = "mdi-book",
    ) -> "ForeignLedger":
        """别人的账本（本地账本挂在 owner 名下），我是其中的成员（或被邀请但未接受）。"""
        ledger_id = self.db.seed_ledger(owner.local_id, name, icon=icon)
        owner_member = self.own_ledger(owner, ledger_id, name, icon=icon)
        my_member = self.add_member(ledger_id, me, role=my_role, status=my_status)
        return ForeignLedger(ledger_id, owner_member, my_member)


@dataclass(frozen=True)
class ForeignLedger:
    ledger_id: str
    owner_member_id: str
    my_member_id: str
