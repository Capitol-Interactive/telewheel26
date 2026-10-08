// Copyright 2026 Capitol Interactive LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Usage: SyntaxCheck "SYMBOL1;SYMBOL2" file.cs [file.cs ...]
// Prints each syntax error and exits 1 if there were any.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: SyntaxCheck \"SYMBOL1;SYMBOL2\" file.cs [file.cs ...]");
            return 2;
        }
        string[] symbols = args[0].Split(';', StringSplitOptions.RemoveEmptyEntries);
        var options = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular, symbols);
        int errors = 0;
        foreach (string path in args.Skip(1))
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), options, path);
            foreach (Diagnostic diagnostic in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                Console.WriteLine(diagnostic);
                errors++;
            }
        }
        Console.WriteLine(errors == 0 ? "syntax OK" : errors + " syntax error(s)");
        return errors == 0 ? 0 : 1;
    }
}
