@echo off
REM Shim so you can type ".\build" instead of ".\build.ps1".
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
