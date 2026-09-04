# iOS 证书配置指南

## ✅ 已生成的文件

| 文件 | 说明 | 用途 |
|------|------|------|
| `ios_distribution.key` | 私钥文件 | 签名用，**不要泄露** |
| `ios_distribution.csr` | 证书签名请求 | 上传到 Apple Developer |
| `csr.conf` | CSR 配置文件 | 生成 CSR 用 |

---

## 📋 接下来的步骤

### 步骤 1：在 Apple Developer 创建证书

1. **打开浏览器访问**：
   ```
   https://developer.apple.com/account/resources/certificates/list
   ```

2. **登录你的 Apple Developer 账号**

3. **点击左上角 `+` 按钮创建证书**

4. **选择证书类型**：
   - 如果是个人账号：选择 `Apple Distribution`
   - 如果是企业账号：选择 `iOS Distribution (Ad Hoc and In House)`

5. **上传 CSR 文件**：
   - 点击「Choose File」
   - 选择 `ios_distribution.csr` 文件
   - 点击「Continue」

6. **下载证书**：
   - 下载生成的 `ios_distribution.cer` 文件
   - 保存到 `build/certificates/` 目录

---

### 步骤 2：创建 App ID

1. **访问**：
   ```
   https://developer.apple.com/account/resources/identifiers/list
   ```

2. **点击 `+` 创建新的 App ID**

3. **选择 `App IDs` → `App`**

4. **填写信息**：
   - Description: `FinanceTracker`
   - Bundle ID: `com.financetracker.app`

5. **勾选需要的功能**（如推送通知等）

6. **点击「Continue」并注册**

---

### 步骤 3：创建 Provisioning Profile

1. **访问**：
   ```
   https://developer.apple.com/account/resources/profiles/list
   ```

2. **点击 `+` 创建新的 Profile**

3. **选择类型**：
   - 个人账号：选择 `Ad Hoc` (用于测试分发)
   - 企业账号：选择 `In House` (用于内部分发)

4. **选择 App ID**：
   - 选择刚才创建的 `com.financetracker.app`

5. **选择证书**：
   - 选择刚才创建的 Distribution 证书

6. **选择设备**（Ad Hoc 类型需要）：
   - 添加测试设备的 UDID
   - 或者选择所有设备（如果有企业账号）

7. **填写 Profile 名称**：
   - 名称：`FinanceTracker Distribution`

8. **下载 Profile**：
   - 下载 `.mobileprovision` 文件
   - 保存到 `build/certificates/` 目录

---

### 步骤 4：转换证书格式

将下载的证书转换为 GitHub Actions 需要的格式。

在 Windows 上执行：

```bash
cd build/certificates

# 将 .cer 转换为 PEM 格式
openssl x509 -in ios_distribution.cer -inform DER -out ios_distribution.pem -outform PEM

# 合并证书和私钥为 .p12 文件
# 会提示设置导出密码，记住这个密码！
openssl pkcs12 -export -inkey ios_distribution.key -in ios_distribution.pem -out ios_distribution.p12 -password pass:financeTracker
```

---

### 步骤 5：Base64 编码文件

```bash
# 编码 .p12 证书
base64 -i ios_distribution.p12 > certificate_base64.txt
cat certificate_base64.txt | clip  # 复制到剪贴板

# 编码 .mobileprovision 文件
base64 -i YourProfile.mobileprovision > profile_base64.txt
cat profile_base64.txt | clip  # 复制到剪贴板
```

---

### 步骤 6：配置 GitHub Secrets

打开你的 GitHub 仓库：
```
https://github.com/你的用户名/finance-tracker
```

进入 `Settings` → `Secrets and variables` → `Actions`

添加以下 Secrets：

| Secret 名称 | 值 |
|-------------|-----|
| `APPLE_CERTIFICATE` | 粘贴 `certificate_base64.txt` 的内容 |
| `APPLE_CERTIFICATE_PASSWORD` | `financeTracker` (或你设置的密码) |
| `APPLE_PROVISIONING_PROFILE` | 粘贴 `profile_base64.txt` 的内容 |
| `APPLE_PROVISIONING_PROFILE_NAME` | `FinanceTracker Distribution` |
| `PGYER_API_KEY` | `4b0051a793157929de2ee61a4a3892ce` |

---

## 🎯 快速参考

### 需要访问的 Apple 网站

| 功能 | 网址 |
|------|------|
| 证书管理 | https://developer.apple.com/account/resources/certificates/list |
| App ID 管理 | https://developer.apple.com/account/resources/identifiers/list |
| Profile 管理 | https://developer.apple.com/account/resources/profiles/list |
| 设备管理 | https://developer.apple.com/account/resources/devices/list |

---

## ⚠️ 注意事项

1. **私钥安全**：`ios_distribution.key` 文件非常重要，不要泄露或丢失
2. **证书有效期**：Distribution 证书有效期为 1 年，需要定期更新
3. **Profile 有效期**：Provisioning Profile 有效期为 1 年
4. **Bundle ID 必须匹配**：Profile 中的 App ID 必须与项目的 `ApplicationId` 一致

---

## ✅ 完成后的下一步

配置完成后，告诉我，我会帮你：

1. 执行一次测试构建
2. 验证证书是否正确
3. 上传到蒲公英
