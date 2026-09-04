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

**值**: `4b0051a793157929de2ee61a4a3892ce`（已提供）

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

构建完成后，可以在以下位置找到：

1. **蒲公英下载链接**：在 Actions 日志中查看
2. **GitHub Artifacts**：Actions 页面的 Artifacts 部分

---

## ⚠️ 常见问题

### Q: 证书导入失败

A: 确保证书是 **Distribution** 类型（不是 Development），且未过期。

### Q: Provisioning Profile 不匹配

A: 确保 Profile 中包含的 App ID 与项目中的 `ApplicationId` 一致：
```
com.financetracker.app
```

### Q: IPA 生成失败

A: 检查以下几点：
- 证书和 Profile 是否匹配
- Profile 是否包含正确的设备 UDID（如果是 Ad Hoc）
- Bundle ID 是否正确

### Q: 蒲公英上传失败

A: 检查 API Key 是否正确，网络是否正常。

---

## 🔐 安全建议

1. **证书密码**：使用强密码，不要泄露
2. **定期轮换**：建议每 6 个月更换一次证书
3. **最小权限**：只授予必要的证书权限

---

## 📝 下一步

配置完成后，你可以：

1. 手动触发一次构建测试
2. 配置自动触发（如 push 到特定分支）
3. 添加构建通知（邮件/Slack/钉钉）

需要帮助配置自动触发或通知吗？
