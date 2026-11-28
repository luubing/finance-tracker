# iOS 自动打包配置指南

## 📋 需要配置的 GitHub Secrets

在 GitHub 仓库的 `Settings` → `Secrets and variables` → `Actions` 中添加以下 Secrets：

### 1. APPLE_CERTIFICATE

**说明**: Apple Distribution 证书（.p12 文件的 Base64 编码）

**获取方式**:
1. 在 Mac 上打开「钥匙串访问」
2. 选择「我的证书」，找到你的 Distribution 证书
3. 右键导出为 `.p12` 文件
4. 设置密码并记住
5. 终端执行 Base64 编码：

```bash
base64 -i certificate.p12 | pbcopy
```

6. 粘贴到 Secret 值

---

### 2. APPLE_CERTIFICATE_PASSWORD

**说明**: 导出 .p12 证书时设置的密码

**示例**: `your_certificate_password`

---

### 3. APPLE_PROVISIONING_PROFILE

**说明**: Provisioning Profile 文件的 Base64 编码

**获取方式**:
1. 登录 [Apple Developer](https://developer.apple.com/account/resources/profiles/list)
2. 下载你的 App Distribution Provisioning Profile
3. 终端执行 Base64 编码：

```bash
base64 -i YourProfile.mobileprovision | pbcopy
```

4. 粘贴到 Secret 值

---

### 4. APPLE_PROVISIONING_PROFILE_NAME

**说明**: Provisioning Profile 的名称（不是文件名）

**获取方式**:
在 Mac 上终端执行：
```bash
security cms -D -i YourProfile.mobileprovision | grep -A1 Name
```

或者在 Xcode 中查看 Profile 详情。

**示例**: `FinanceTracker App Distribution`

---

### 5. PGYER_API_KEY

**说明**: 蒲公英 API Key

**值**: 蒲公英后台「API 信息」中的 API Key（仅配置在 GitHub Secrets，切勿写入仓库）

---

### 6. PGYER_INSTALL_PASSWORD（可选）

**说明**: 上传到蒲公英的应用的安装密码（装包时要输入）。**这一项可以不设**：不设时工作流用「公开安装」上传，知道下载页地址的人都能下载、安装；设了就改为密码安装。

**值**: 想限制谁能装时，自己定一个。本仓库是公开的，工作流日志任何人都能看，所以密码**只放在这个 Secret 里**——工作流不会把它打印出来，也不要写进代码或文档。想换密码时，改这个 Secret 即可，下次构建生效；想改回公开安装，删掉这个 Secret 即可。

> ⚠️ 公开安装时，下载页地址就相当于访问凭证：请不要把它写进 Issue / PR / 日志。安装包里内嵌的描述文件含有设备 UDID 和 Team ID，下载页地址一旦流传，这些信息也会随安装包被拿到。

---

### 7. CLOUD_API_BASE_URL

**说明**: 打出的 App 连接哪台云端服务器（API 根地址）。仓库里没有内置线上域名，每次打包由这里注入。

**值**: 形如 `https://api.example.com/`（必须是 `http://` 或 `https://` 开头的地址，不要带账号密码、`?` 参数；末尾的 `/api` 会被自动去掉）。

**配置位置**: 同一页面，**Secret 或 Variable 二选一**都行（同名时 Secret 优先）。两种放法，构建日志里这个地址都会被遮成 `***`（工作流会显式登记脱敏，因为 Variable 本身不会被 GitHub 自动遮）。

> 💡 这个地址只是「不写进仓库」，并不是真正的机密：它会被编译进安装包，装了 App 的人能读出来。

---

> 💡 工作流的第一步会检查上面这些必填的 Secret / 变量（除了标明「可选」的 `PGYER_INSTALL_PASSWORD`）是否齐全、地址格式是否正确，缺什么就直接报错并列出名字，不会等到打包到一半才失败。

---

## 🚀 使用方法

### 方式一：手动触发（推荐）

1. 进入 GitHub 仓库页面
2. 点击 `Actions` 标签
3. 选择 `Build iOS and Upload to Pgyer` 工作流
4. 点击 `Run workflow`
5. 可选填写版本号和构建号（留空则自动递增）
6. 点击 `Run workflow` 按钮

### 方式二：命令行触发

```bash
# 使用 GitHub CLI
gh workflow run build-ios.yml \
  --field version=1.0.0 \
  --field build_number=10
```

---

## 📦 构建产物

构建完成后，安装包在 **蒲公英后台「应用管理」**里：下载页地址和二维码都在那里。设置了 `PGYER_INSTALL_PASSWORD` 时，安装时输入这个密码；没设置则是公开安装，直接装。

出于安全考虑，工作流**不会**把下载地址打印在日志里，也**不会**把 IPA 作为 GitHub Artifact 上传——公开仓库的日志和 Artifact 任何人都能查看 / 下载，而 IPA 里带着签名信息。另外，打包时 Xamarin 的签名任务会固定打印证书主体（含公司 / 个人名）、Team ID、证书指纹，工作流在「Mask signing identity in logs」步骤里先把它们登记成遮罩，日志里显示为 `***`。

---

## ⚠️ 常见问题

### Q: 证书导入失败

A: 确保证书是 **Distribution** 类型（不是 Development），且未过期。

### Q: Provisioning Profile 不匹配

A: 确保 Profile 中包含的 App ID 与项目中的 `ApplicationId` 一致（见 `src/FinanceTracker.App/FinanceTracker.App.csproj`）：
```
com.finance.tracker.app
```

### Q: IPA 生成失败

A: 检查以下几点：
- 证书和 Profile 是否匹配
- Profile 是否包含正确的设备 UDID（如果是 Ad Hoc）
- Bundle ID 是否正确

### Q: 蒲公英上传失败

A: 检查 API Key 是否正确，网络是否正常。

### Q: 工作流第一步就报「缺少必需的 Secret / 变量」

A: 按报错里列出的名字，到 `Settings` → `Secrets and variables` → `Actions` 补上。`CLOUD_API_BASE_URL` 可以放在 Secrets，也可以放在 Variables 标签页。

### Q: 报「CLOUD_API_BASE_URL 必须是以 http:// 或 https:// 开头的地址」

A: 值必须是 `http://` 或 `https://` 开头的地址，不能带账号密码（`user:pw@`）、`?` 参数和 `#` 片段，例如 `https://api.example.com/`。

---

## 🔐 安全建议

1. **证书密码**：使用强密码，不要泄露；`build/certificates/convert.bat` 默认会生成随机密码
2. **密码不入库**：`.p12` 密码、蒲公英安装密码（如果设置了）、API Key 都只放 GitHub Secrets，不写进仓库、Issue 或 PR 描述；本仓库公开，工作流日志任何人都能看（工作流本身也不再上传 Artifact）
3. **定期轮换**：建议每 6 个月更换一次证书；换密码的步骤见 [`build/certificates/README.md`](../build/certificates/README.md)
4. **最小权限**：只授予必要的证书权限；工作流本身只申请了 `contents: read`
5. **一旦怀疑泄露**：立刻改蒲公英安装密码（如果设置了）、重新导出 `.p12` 并更新对应 Secret；必要时在 Apple Developer 吊销证书
