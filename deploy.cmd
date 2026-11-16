@echo off
rem Runs deploy.ps1 even when PowerShell scripts are blocked by execution policy.
rem   deploy            build into %LOCALAPPDATA%\Programs\TodayChecklist, used from the next app start
rem   deploy -Restart   same, then close the running app (it saves, like clicking X) and open the new build
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy.ps1" %*
