# -*- coding: utf-8 -*-
"""
支付渠道页 E2E（iOS UI 重构 · 14 号 ticket）：二级页面模板 + 「自定义 / 预设」分组 + 底部 Sheet 编辑 + 左滑操作 + 默认渠道。

只覆盖网页层——Android / iOS 上的真机手感（左滑跟手、Sheet 弹簧、键盘避让、返回手势）见 ticket 14 的人工验收清单。
与分类管理页同构，两个页面共用 managed_items_base.py 里按 Spec 参数化的检查：

  A. 二级页面外壳：Tab 栏隐藏；玻璃导航栏（返回 + 居中内联标题 + 右上角「＋」）；预设渠道分组（只读、标注「预设」、无默认徽标）；
     自定义分组只有「新建支付渠道」行；返回按钮 / 边缘右滑返回
  B. 新建（底部 Sheet）：名称校验（空 / 全空格 / 超长）、选图标（24 个候选）、保存后出现在自定义分组（0 笔）、表单每次打开都是干净的
  C. 编辑：点按整行进入 Sheet，改名称 / 图标 / 排序，列表按排序重排；历史数据里非法的图标类名回退为默认图标
  D. 默认渠道：Sheet 里的开关 → 「默认」徽标，同一时刻只有一个默认；预设渠道点按弹出菜单，只能设为 / 取消默认
  E. 左滑：展开前操作按钮点不到；纵向滑动不展开；展开后「编辑」「删除」可点；展开时点内容只收起
  F. 删除：Alert 确认（取消 / 确认）、软删除；渠道下有账单时被拦截且保留；编辑 Sheet 里的「删除」是非滑动入口
  G. 图标：候选图标与列表里的图标在 MDI 7.2.96 字体里都有字形
  H. 可见性：别人的自定义渠道与已软删除的不显示
  I. 「我的」页入口行摘要与库里一致，新建后返回自动刷新
  J. Sheet 的收起方式：点遮罩、Esc；在名称框里按回车等同保存

可观察属性（已登记到 README）：payment-channels-page、channel-add / channel-add-row、channel-row[data-channel-id][data-open]、
channel-cell、preset-cell、badge-default、custom-channels / preset-channels、channel-sheet(-host)、channel-name / channel-sort /
channel-form-error / channel-save、channel-default-switch、channel-delete-cell、channel-delete-alert、channel-preset-sheet / preset-default、
swipe-edit / swipe-delete、nav-back / nav-title、icon-picker。
"""
import sys

import managed_items_base as base
from e2e_base import LocalDb, TestRun, login, require_server, session
from managed_items_base import Spec

SPEC = Spec(
    key="channel",
    noun="支付渠道",
    entry_label="支付渠道",
    entry_id="payment-channels",
    path="/payment-channels",
    page_id="payment-channels-page",
    title="支付渠道",
    group_custom="custom-channels",
    group_preset="preset-channels",
    default_icon="mdi-credit-card",
    icon_count=24,
    icon_a="mdi-bank",
    icon_b="mdi-gold",
    add_label="新建支付渠道",
    preset_footer="预设渠道不能修改或删除，点按可设为默认。",
    preset_menu_message="预设渠道不能修改或删除",
    preset_menu_label="设为默认支付渠道",
    placeholder="例如：公司报销卡",
    seed=lambda db, uid, name, **kw: db.seed_channel(uid, name, **kw),
    row=lambda db, item_id: db.channel_row(item_id),
    default_id=lambda db, uid: db.default_channel_id(uid),
    customs=lambda db, uid: db.custom_channels(uid),
    visible_total=lambda db, uid: db.visible_channel_count(uid),
    seed_bill=lambda db, uid, item_id, ledger_id, note: db.seed_bill(uid, ledger_id=ledger_id, channel_id=item_id, note=note),
    preset_id=lambda db, name: db.channel_id_by_name(name),
)


def main() -> int:
    require_server()
    run = TestRun("payment-channels-page")
    db = LocalDb()
    db.verify_schema()

    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)

        base.check_shell(run, page, db, SPEC, db.preset_channel_names())
        name, item_id = base.check_create(run, page, db, user_id, SPEC)
        first, second, second_id = base.check_edit_and_order(run, page, db, user_id, SPEC, name, item_id)
        base.check_default(run, page, db, user_id, SPEC, first, item_id, second, second_id)
        base.check_swipe_and_delete(run, page, db, user_id, SPEC)
        base.check_icons_render(run, page, SPEC)
        base.check_visibility(run, page, db, user_id, SPEC, lambda: db.visible_channel_count(user_id))
        base.check_sheet_dismiss(run, page, db, user_id, SPEC)
        base.check_profile_summary(run, page, db, user_id, SPEC)

        run.check_clean(s.errors)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
