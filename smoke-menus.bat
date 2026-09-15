@echo off
rem The menus chapter of the smoke run: the hangar window, the pause menu and the nav map, shot as menus_*.png.
start "" "%~dp0Builds\Windows\BeltRunner.exe" -smoke menus -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile Logs/smoke-menus.log
