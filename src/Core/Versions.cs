// =====================================================================================
//  Versions.cs - the version number of each program in the toolset
// -------------------------------------------------------------------------------------
//  Each tool has its own version, shown in its About box (GUI) and by --version
//  (command line), and stored in the .exe file (right-click > Properties > Details on
//  Windows, via the [assembly: AssemblyVersion] lines in each program's main source
//  file). Change the number here and in those lines together when a tool is released.
//
//     Compiler    ZX81BasicCompiler.exe and zx81c.exe     (src/Compiler/Cli.cs)
//     Decompiler  ZX81Decompiler.exe and zx81dc.exe       (src/Decompiler/*.cs)
//     Browser     ZX81PBrowser.exe                        (src/Browser/BrowserForm.cs)
// =====================================================================================
namespace ZX81Compiler
{
    public static class Versions
    {
        /// <summary>ZX81 BASIC Compiler (GUI and command line).</summary>
        public const string Compiler = "1.00";
        /// <summary>ZX81 Decompiler (GUI and command line).</summary>
        public const string Decompiler = "1.00";
        /// <summary>.P File Browser.</summary>
        public const string Browser = "1.00";

        /// <summary>The form shown to users: "V1.00".</summary>
        public static string Show(string v) { return "V" + v; }
    }
}
