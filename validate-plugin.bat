@echo off
chcp 65001 >nul
setlocal
REM req-107 B9：插件声明自检入口（双击运行）。内部调主程序 --validate-plugin，与运行期装载共用同一份校验代码。
REM 用法：validate-plugin.bat [defaults.json 路径或插件目录]；不带参数时校验当前目录的 defaults.json。

set "EXE=%~dp0src\UsageMonitor.App\bin\Debug\net8.0-windows\UsageMonitor.App.exe"
if not exist "%EXE%" set "EXE=%~dp0src\UsageMonitor.App\bin\Release\net8.0-windows\UsageMonitor.App.exe"

if not exist "%EXE%" (
    echo [ERROR] 未找到 UsageMonitor.App.exe，请先执行 dotnet build 再运行本脚本。
    pause
    exit /b 1
)

echo 正在校验插件声明...
"%EXE%" --validate-plugin %*
set "RESULT=%ERRORLEVEL%"

echo.
if %RESULT%==0 (
    echo 校验通过。
) else (
    echo 校验未通过，退出码 %RESULT%。
)
pause
exit /b %RESULT%
