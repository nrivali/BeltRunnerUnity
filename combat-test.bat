@echo off
rem Boots the built player straight into a raider fight in a sandbox that never touches the save. F9 jumps to the next hold.
start "" "%~dp0Builds\Windows\BeltRunner.exe" -combat %*
