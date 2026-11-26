#!/bin/sh
# Builds the five programs of the ZX81 toolset with Mono, compiling against the
# .NET Framework 4.0 reference assemblies so they only use APIs that exist in
# Windows' built-in .NET Framework (see CLAUDE.md).
set -e
cd "$(dirname "$0")"
mkdir -p build
REF=/usr/lib/mono/4.0-api
LIBS="-nostdlib -r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll"
GUI="-target:winexe -r:$REF/System.Windows.Forms.dll -r:$REF/System.Drawing.dll"
CS="mcs -langversion:5 -sdk:4 -nowarn:414,219,168"
CORE=$(ls src/Core/*.cs)
UI=$(ls src/UI/*.cs)
$CS $LIBS -out:build/zx81c.exe -main:ZX81Compiler.CliMain $CORE src/Compiler/Cli.cs
$CS $LIBS -out:build/zx81dc.exe -main:ZX81Compiler.DecompilerCli $CORE src/Decompiler/DecompilerCli.cs
$CS $LIBS $GUI -out:build/ZX81BasicCompiler.exe -main:ZX81Compiler.Program $CORE $UI src/Compiler/Cli.cs src/Compiler/MainForm.cs
$CS $LIBS $GUI -out:build/ZX81Decompiler.exe -main:ZX81Compiler.DecompilerProgram $CORE $UI src/Decompiler/DecompilerForm.cs
$CS $LIBS $GUI -out:build/ZX81PBrowser.exe -main:ZX81Compiler.BrowserProgram $CORE $UI src/Browser/*.cs
echo "Built: $(ls build/*.exe | tr '\n' ' ')"
