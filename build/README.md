# FinanceTracker 打包脚本

本目录包含 FinanceTracker 应用的打包和上传脚本。

## 📁 文件说明

| 文件 | 说明 |
|------|------|
| `config.example.env` | 配置文件模板 |
| `config.env` | 实际配置文件（需自行创建） |
| `build.sh` | Linux/macOS 打包脚本 |
| `build.ps1` | Windows PowerShell 打包脚本 |
| `myapp.keystore` | Android 签名密钥（需自行生成） |

## 🚀 快速开始

### 1. 创建配置文件

```bash
# 复制配置文件模板
cp config.example.env config.env

# 编辑配置文件，填写必要的配置项
```

### 2. 配置说明

#### 必填配置

| 配置项 | 说明 | 示例 |
|--------|------|------|
| `ANDROID_KEYSTORE_PATH` | Keystore 文件路径 | `build/myapp.keystore` |
| `ANDROID_KEYSTORE_PASSWORD` | Keystore 密码 | `your_password` |
| `ANDROID_KEY_ALIAS` | 密钥别名 | `myapp` |
| `ANDROID_KEY_PASSWORD` | 密钥密码 | `your_password` |
| `PGYER_API_KEY` | 蒲公英 API Key | `xxxxxxxx` |

#### 可选配置

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `PGYER_INSTALL_PASSWORD` | 蒲公英安装密码 | 空（无需密码） |
| `AUTO_INCREMENT_VERSION` | 自动递增版本号 | `true` |
| `CUSTOM_VERSION` | 自定义版本号 | 使用 csproj 中的版本 |
| `CUSTOM_BUILD_NUMBER` | 自定义构建号 | 自动递增 |

### 3. 生成 Android 签名密钥

如果还没有 Keystore 文件，使用以下命令生成：

```bash
keytool -genkey -v -keystore build/myapp.keystore -alias myapp -keyalg RSA -keysize 2048 -validity 10000
```

按照提示输入密码和证书信息。

### 4. 获取蒲公英 API Key

1. 登录 [蒲公英](https://www.pgyer.com)
2. 进入「账户设置」>「API 信息」
3. 复制 `API Key`

## 📦 使用方法

### Windows (PowerShell)

```powershell
# 打包 Android 并上传
.\build\build.ps1 -Android

# 仅打包 Android，不上传
.\build\build.ps1 -Android -NoUpload

# 打包所有平台并上传
.\build\build.ps1 -All

# 显示帮助
.\build\build.ps1 -Help
```

### Linux/macOS (Bash)

```bash
# 添加执行权限
chmod +x build/build.sh

# 打包 Android 并上传
./build/build.sh --android

# 仅打包 Android，不上传
./build/build.sh --android --no-upload

# 打包所有平台并上传
./build/build.sh --all
```

## 📋 打包产物

打包完成后，产物位于 `build/output/` 目录：

```
build/output/
├── android/              # Android APK 文件
│   └── com.financetracker.app.apk
├── ios/                  # iOS IPA 文件 (仅 macOS)
│   └── FinanceTracker.App.ipa
├── android_apk_path.txt  # APK 文件路径
└── ios_ipa_path.txt      # IPA 文件路径
```

## 🔧 常见问题

### Q: Android 打包失败，提示签名错误

A: 检查 `config.env` 中的 Keystore 配置是否正确，确保 Keystore 文件存在且密码正确。

### Q: iOS 打包失败

A: iOS 打包需要在 macOS 上进行，需要：
- 安装 Xcode
- Apple 开发者账号
- 有效的签名证书和 Provisioning Profile

### Q: 上传到蒲公英失败

A: 检查：
1. `PGYER_API_KEY` 是否正确
2. 网络连接是否正常
3. 文件大小是否超过限制（免费版 100MB）

### Q: 如何自定义版本号

A: 在 `config.env` 中设置：
```bash
CUSTOM_VERSION=1.2.0
CUSTOM_BUILD_NUMBER=100
```

或设置 `AUTO_INCREMENT_VERSION=false` 使用 csproj 中的版本。

## 📝 注意事项

1. **首次打包前**必须生成 Android Keystore 并配置
2. **iOS 打包**仅支持 macOS 环境
3. **版本号管理**建议使用自动递增
4. **蒲公英免费版**有上传次数和大小限制
5. **生产环境打包**建议关闭 `usesCleartextTraffic`

## 🔗 相关链接

- [蒲公英官方文档](https://www.pgyer.com/doc/view/api)
- [MAUI 发布文档](https://learn.microsoft.com/zh-cn/dotnet/maui/android/deployment/publish-cli)
- [Android 签名文档](https://developer.android.com/studio/publish/app-signing)
