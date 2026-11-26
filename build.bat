@echo off
rem Builds the five programs of the ZX81 toolset with the C# compiler that ships with
rem .NET Framework 4 (no Visual Studio needed).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist build mkdir build
set GUI=/target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll
set CORE=src\Core\*.cs
set UI=src\UI\*.cs
"%CSC%" /nologo /out:build\zx81c.exe /main:ZX81Compiler.CliMain %CORE% src\Compiler\Cli.cs || exit /b 1
"%CSC%" /nologo /out:build\zx81dc.exe /main:ZX81Compiler.DecompilerCli %CORE% src\Decompiler\DecompilerCli.cs || exit /b 1
"%CSC%" /nologo %GUI% /out:build\ZX81BasicCompiler.exe /main:ZX81Compiler.Program %CORE% %UI% src\Compiler\Cli.cs src\Compiler\MainForm.cs || exit /b 1
"%CSC%" /nologo %GUI% /out:build\ZX81Decompiler.exe /main:ZX81Compiler.DecompilerProgram %CORE% %UI% src\Decompiler\DecompilerForm.cs || exit /b 1
"%CSC%" /nologo %GUI% /out:build\ZX81PBrowser.exe /main:ZX81Compiler.BrowserProgram %CORE% %UI% src\Browser\*.cs || exit /b 1
echo Built the five programs in the build folder.
