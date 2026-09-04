@echo off
echo ===========================================
echo iOS Certificate Converter
echo ===========================================
echo.

set OPENSSL="C:\Program Files\Git\mingw64\bin\openssl.exe"
set CERT_FILE=ios_distribution.cer
set PROFILE_FILE=FinanceTracker_Distribution.mobileprovision
set P12_PASSWORD=financeTracker

echo [1/5] Check files...

if not exist "%CERT_FILE%" (
    echo ERROR: Certificate file not found: %CERT_FILE%
    echo Please download from Apple Developer and put in this folder
    pause
    exit /b 1
)

if not exist "%PROFILE_FILE%" (
    echo ERROR: Profile file not found: %PROFILE_FILE%
    echo Please download from Apple Developer and put in this folder
    pause
    exit /b 1
)

if not exist "ios_distribution.key" (
    echo ERROR: Private key not found: ios_distribution.key
    pause
    exit /b 1
)

echo OK: All files found
echo.

echo [2/5] Convert certificate format...
%OPENSSL% x509 -in "%CERT_FILE%" -inform DER -out ios_distribution.pem -outform PEM
if errorlevel 1 (
    echo ERROR: Certificate conversion failed
    pause
    exit /b 1
)
echo OK: Certificate converted
echo.

echo [3/5] Generate .p12 file...
%OPENSSL% pkcs12 -export -inkey ios_distribution.key -in ios_distribution.pem -out ios_distribution.p12 -passout pass:%P12_PASSWORD%
if errorlevel 1 (
    echo ERROR: .p12 generation failed
    pause
    exit /b 1
)
echo OK: .p12 file generated
echo.

echo [4/5] Base64 encode certificate...
certutil -encode ios_distribution.p12 certificate_base64.txt >nul
echo OK: Certificate encoded
echo.

echo [5/5] Base64 encode profile...
certutil -encode "%PROFILE_FILE%" profile_base64.txt >nul
echo OK: Profile encoded
echo.

echo ===========================================
echo SUCCESS! All files generated.
echo ===========================================
echo.
echo Files created:
echo   - ios_distribution.p12
echo   - certificate_base64.txt
echo   - profile_base64.txt
echo.
echo Next steps:
echo   1. Copy certificate_base64.txt content to GitHub Secret: APPLE_CERTIFICATE
echo   2. Copy profile_base64.txt content to GitHub Secret: APPLE_PROVISIONING_PROFILE
echo   3. Set APPLE_CERTIFICATE_PASSWORD to: %P12_PASSWORD%
echo.
pause
