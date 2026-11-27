@echo off
setlocal

echo ===========================================
echo iOS Certificate Converter
echo ===========================================
echo.

set CERT_FILE=ios_distribution.cer
set PROFILE_FILE=FinanceTracker_Distribution.mobileprovision

rem ---- openssl: OPENSSL_BIN (full path) > openssl on PATH > the copy bundled with Git for Windows ----
set "OPENSSL="
rem (keep these two checks flat: "exit /b 1" in a nested block that has more commands after it returns exit code 0)
if defined OPENSSL_BIN if not exist "%OPENSSL_BIN%" (
    echo ERROR: OPENSSL_BIN does not point to an existing file
    pause
    exit /b 1
)
if defined OPENSSL_BIN set "OPENSSL=%OPENSSL_BIN%"
if not defined OPENSSL where openssl >nul 2>nul && set "OPENSSL=openssl"
if not defined OPENSSL if exist "%ProgramFiles%\Git\mingw64\bin\openssl.exe" set "OPENSSL=%ProgramFiles%\Git\mingw64\bin\openssl.exe"
if not defined OPENSSL (
    echo ERROR: openssl not found.
    echo Install Git for Windows, or set OPENSSL_BIN to the full path of openssl.exe
    pause
    exit /b 1
)

rem ---- .p12 export password ----
rem Never hard-code it: use the P12_PASSWORD environment variable if you set one,
rem otherwise generate a random one for this run. It is passed to openssl through the
rem environment (not the command line). A generated one touches the disk only through
rem a temp file in %TEMP% that is deleted right after it is read.
set "P12_PASSWORD_GENERATED="
if not defined P12_PASSWORD (
    "%OPENSSL%" rand -hex 16 > "%TEMP%\ft_p12_pw.tmp"
    set /p P12_PASSWORD=<"%TEMP%\ft_p12_pw.tmp"
    del "%TEMP%\ft_p12_pw.tmp" >nul 2>nul
    set "P12_PASSWORD_GENERATED=1"
)
if not defined P12_PASSWORD (
    echo ERROR: could not generate a .p12 password
    pause
    exit /b 1
)

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
    echo See README.md, step 0, for how to generate the key and the CSR
    pause
    exit /b 1
)

echo OK: All files found
echo.

echo [2/5] Convert certificate format...
"%OPENSSL%" x509 -in "%CERT_FILE%" -inform DER -out ios_distribution.pem -outform PEM
if errorlevel 1 (
    echo ERROR: Certificate conversion failed
    pause
    exit /b 1
)
echo OK: Certificate converted
echo.

echo [3/5] Generate .p12 file...
"%OPENSSL%" pkcs12 -export -inkey ios_distribution.key -in ios_distribution.pem -out ios_distribution.p12 -passout env:P12_PASSWORD
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
echo Files created (all git-ignored, keep them private):
echo   - ios_distribution.p12
echo   - certificate_base64.txt
echo   - profile_base64.txt
echo.
echo Next steps:
echo   1. Copy certificate_base64.txt content to GitHub Secret: APPLE_CERTIFICATE
echo   2. Copy profile_base64.txt content to GitHub Secret: APPLE_PROVISIONING_PROFILE
if defined P12_PASSWORD_GENERATED (
    echo   3. Set GitHub Secret APPLE_CERTIFICATE_PASSWORD to the random password below.
    echo      It is shown only this once and is NOT saved anywhere - paste it into the secret now.
    echo.
    echo        %P12_PASSWORD%
) else (
    echo   3. Set GitHub Secret APPLE_CERTIFICATE_PASSWORD to the P12_PASSWORD you provided.
)
echo.
pause
