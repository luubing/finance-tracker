#!/bin/bash

# ===========================================
# FinanceTracker 打包上传脚本
# 支持 Android/iOS 打包并上传到蒲公英
# ===========================================

set -e

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 项目路径
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
CONFIG_FILE="$SCRIPT_DIR/config.env"

# 打印带颜色的消息
print_info() { echo -e "${BLUE}[INFO]${NC} $1"; }
print_success() { echo -e "${GREEN}[SUCCESS]${NC} $1"; }
print_warning() { echo -e "${YELLOW}[WARNING]${NC} $1"; }
print_error() { echo -e "${RED}[ERROR]${NC} $1"; }

# 打印分隔线
print_separator() { echo "=========================================="; }

# 加载配置文件
load_config() {
    if [ ! -f "$CONFIG_FILE" ]; then
        print_error "配置文件不存在: $CONFIG_FILE"
        print_info "请复制 config.example.env 为 config.env 并填写配置"
        exit 1
    fi
    source "$CONFIG_FILE"
    print_success "配置文件加载成功"
}

# 检查依赖
check_dependencies() {
    print_info "检查依赖..."

    if ! command -v dotnet &> /dev/null; then
        print_error "dotnet SDK 未安装"
        exit 1
    fi

    if ! command -v curl &> /dev/null; then
        print_error "curl 未安装"
        exit 1
    fi

    # Android 打包检查
    if [ "$BUILD_ANDROID" = "true" ]; then
        if [ ! -f "$PROJECT_ROOT/$ANDROID_KEYSTORE_PATH" ]; then
            print_error "Android Keystore 文件不存在: $ANDROID_KEYSTORE_PATH"
            exit 1
        fi
    fi

    # iOS 打包检查 (仅 macOS)
    if [ "$BUILD_IOS" = "true" ] && [[ "$OSTYPE" != "darwin"* ]]; then
        print_error "iOS 打包仅支持 macOS 系统"
        exit 1
    fi

    print_success "依赖检查通过"
}

# 获取版本信息
get_version_info() {
    CSPROJ_FILE="$PROJECT_ROOT/src/FinanceTracker.App/FinanceTracker.App.csproj"

    # 从 csproj 读取版本号
    CURRENT_VERSION=$(grep -oP '<ApplicationDisplayVersion>\K[^<]+' "$CSPROJ_FILE" || echo "1.0.0")
    CURRENT_BUILD=$(grep -oP '<ApplicationVersion>\K[^<]+' "$CSPROJ_FILE" || echo "1")

    print_info "当前版本: $CURRENT_VERSION (Build: $CURRENT_BUILD)"

    # 自定义版本号
    if [ -n "$CUSTOM_VERSION" ]; then
        APP_VERSION="$CUSTOM_VERSION"
    else
        APP_VERSION="$CURRENT_VERSION"
    fi

    # 自定义构建号
    if [ -n "$CUSTOM_BUILD_NUMBER" ]; then
        BUILD_NUMBER="$CUSTOM_BUILD_NUMBER"
    elif [ "$AUTO_INCREMENT_VERSION" = "true" ]; then
        BUILD_NUMBER=$((CURRENT_BUILD + 1))
    else
        BUILD_NUMBER="$CURRENT_BUILD"
    fi

    print_info "打包版本: $APP_VERSION (Build: $BUILD_NUMBER)"
}

# 更新 csproj 版本号
update_version() {
    if [ "$AUTO_INCREMENT_VERSION" = "true" ] || [ -n "$CUSTOM_VERSION" ] || [ -n "$CUSTOM_BUILD_NUMBER" ]; then
        print_info "更新版本号..."
        CSPROJ_FILE="$PROJECT_ROOT/src/FinanceTracker.App/FinanceTracker.App.csproj"

        # 使用 sed 更新版本号
        sed -i "s/<ApplicationDisplayVersion>.*<\/ApplicationDisplayVersion>/<ApplicationDisplayVersion>$APP_VERSION<\/ApplicationDisplayVersion>/" "$CSPROJ_FILE"
        sed -i "s/<ApplicationVersion>.*<\/ApplicationVersion>/<ApplicationVersion>$BUILD_NUMBER<\/ApplicationVersion>/" "$CSPROJ_FILE"

        print_success "版本号已更新: $APP_VERSION ($BUILD_NUMBER)"
    fi
}

# 打包 Android
build_android() {
    print_separator
    print_info "开始打包 Android..."
    print_separator

    cd "$PROJECT_ROOT"

    # 设置签名配置
    export AndroidKeyStore=true
    export AndroidSigningKeyStore="$PROJECT_ROOT/$ANDROID_KEYSTORE_PATH"
    export AndroidSigningKeyAlias="$ANDROID_KEY_ALIAS"
    export AndroidSigningKeyPass="$ANDROID_KEY_PASSWORD"
    export AndroidSigningStorePass="$ANDROID_KEYSTORE_PASSWORD"

    # 打包 APK
    print_info "打包 APK..."
    dotnet publish src/FinanceTracker.App/FinanceTracker.App.csproj \
        -f net10.0-android \
        -c Release \
        -o "$PROJECT_ROOT/build/output/android"

    # 查找生成的 APK 文件
    APK_FILE=$(find "$PROJECT_ROOT/build/output/android" -name "*.apk" | head -1)

    if [ -z "$APK_FILE" ]; then
        print_error "APK 文件未找到"
        exit 1
    fi

    print_success "Android APK 打包成功: $APK_FILE"
    echo "$APK_FILE" > "$PROJECT_ROOT/build/output/android_apk_path.txt"
}

# 打包 iOS
build_ios() {
    print_separator
    print_info "开始打包 iOS..."
    print_separator

    cd "$PROJECT_ROOT"

    # 打包 IPA
    print_info "打包 IPA..."
    dotnet publish src/FinanceTracker.App/FinanceTracker.App.csproj \
        -f net10.0-ios \
        -c Release \
        -o "$PROJECT_ROOT/build/output/ios"

    # 查找生成的 IPA 文件
    IPA_FILE=$(find "$PROJECT_ROOT/build/output/ios" -name "*.ipa" | head -1)

    if [ -z "$IPA_FILE" ]; then
        print_error "IPA 文件未找到"
        print_warning "iOS 打包可能需要在 Xcode 中手动归档"
        return 1
    fi

    print_success "iOS IPA 打包成功: $IPA_FILE"
    echo "$IPA_FILE" > "$PROJECT_ROOT/build/output/ios_ipa_path.txt"
}

# 上传到蒲公英
upload_to_pgyer() {
    local file_path="$1"
    local platform="$2"

    print_separator
    print_info "上传 $platform 到蒲公英..."
    print_separator

    if [ ! -f "$file_path" ]; then
        print_error "文件不存在: $file_path"
        return 1
    fi

    # 构建上传参数
    local upload_params="-F \"file=@$file_path\" -F \"_api_key=$PGYER_API_KEY\""

    if [ -n "$PGYER_INSTALL_PASSWORD" ]; then
        upload_params="$upload_params -F \"password=$PGYER_INSTALL_PASSWORD\""
    fi

    if [ -n "$PGYER_CHANNEL_PASSWORD" ]; then
        upload_params="$upload_params -F \"channelPassword=$PGYER_CHANNEL_PASSWORD\""
    fi

    # 上传文件
    print_info "正在上传，请稍候..."
    local response=$(eval curl -w "\"\\n%{http_code}\"" $upload_params \
        "https://www.pgyer.com/apiv2/app/upload" 2>/dev/null)

    local http_code=$(echo "$response" | tail -n1)
    local body=$(echo "$response" | sed '$d')

    if [ "$http_code" = "200" ]; then
        local code=$(echo "$body" | grep -oP '"code":\K[^,}]+' || echo "")
        if [ "$code" = "0" ]; then
            local app_name=$(echo "$body" | grep -oP '"appName":"[^"]*"' | cut -d'"' -f4)
            local build_version=$(echo "$body" | grep -oP '"buildVersion":"[^"]*"' | cut -d'"' -f4)
            local build_key=$(echo "$body" | grep -oP '"buildKey":"[^"]*"' | cut -d'"' -f4)

            print_success "上传成功！"
            print_info "应用名称: $app_name"
            print_info "版本: $build_version"
            print_info "下载地址: https://www.pgyer.com/$build_key"
        else
            local error_msg=$(echo "$body" | grep -oP '"message":"[^"]*"' | cut -d'"' -f4)
            print_error "上传失败: $error_msg"
            return 1
        fi
    else
        print_error "上传失败，HTTP 状态码: $http_code"
        return 1
    fi
}

# 生成构建报告
generate_report() {
    print_separator
    print_info "构建报告"
    print_separator"

    echo "应用名称: 记账本"
    echo "版本号: $APP_VERSION"
    echo "构建号: $BUILD_NUMBER"
    echo "构建时间: $(date '+%Y-%m-%d %H:%M:%S')"
    echo ""

    if [ -f "$PROJECT_ROOT/build/output/android_apk_path.txt" ]; then
        echo "Android APK: $(cat "$PROJECT_ROOT/build/output/android_apk_path.txt")"
    fi

    if [ -f "$PROJECT_ROOT/build/output/ios_ipa_path.txt" ]; then
        echo "iOS IPA: $(cat "$PROJECT_ROOT/build/output/ios_ipa_path.txt")"
    fi

    print_separator"
}

# 主函数
main() {
    print_separator
    print_info "FinanceTracker 打包上传脚本"
    print_separator"

    # 解析参数
    BUILD_ANDROID=false
    BUILD_IOS=false
    UPLOAD_TO_PGYER=true

    while [[ $# -gt 0 ]]; do
        case $1 in
            --android)
                BUILD_ANDROID=true
                shift
                ;;
            --ios)
                BUILD_IOS=true
                shift
                ;;
            --no-upload)
                UPLOAD_TO_PGYER=false
                shift
                ;;
            --all)
                BUILD_ANDROID=true
                BUILD_IOS=true
                shift
                ;;
            *)
                print_error "未知参数: $1"
                echo "用法: $0 [--android] [--ios] [--all] [--no-upload]"
                exit 1
                ;;
        esac
    done

    # 如果没有指定平台，默认打包 Android
    if [ "$BUILD_ANDROID" = "false" ] && [ "$BUILD_IOS" = "false" ]; then
        BUILD_ANDROID=true
    fi

    # 执行流程
    load_config
    check_dependencies
    get_version_info
    update_version

    # 创建输出目录
    mkdir -p "$PROJECT_ROOT/build/output"

    # Android 打包
    if [ "$BUILD_ANDROID" = "true" ]; then
        build_android
        if [ "$UPLOAD_TO_PGYER" = "true" ] && [ -f "$PROJECT_ROOT/build/output/android_apk_path.txt" ]; then
            upload_to_pgyer "$(cat "$PROJECT_ROOT/build/output/android_apk_path.txt")" "Android"
        fi
    fi

    # iOS 打包
    if [ "$BUILD_IOS" = "true" ]; then
        build_ios
        if [ "$UPLOAD_TO_PGYER" = "true" ] && [ -f "$PROJECT_ROOT/build/output/ios_ipa_path.txt" ]; then
            upload_to_pgyer "$(cat "$PROJECT_ROOT/build/output/ios_ipa_path.txt")" "iOS"
        fi
    fi

    # 生成报告
    generate_report

    print_success "所有任务完成！"
}

# 执行主函数
main "$@"
