@echo off
pushd "%~dp0"
call :main %*
popd
goto :EOF

:main
   call msbuild /v:m /t:Restore ^
&& call msbuild /v:m /p:Configuration=Debug ^
&& call msbuild /v:m /p:Configuration=Release
goto :EOF
