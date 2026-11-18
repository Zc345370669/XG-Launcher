using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;
using System.Text.RegularExpressions;
var values=new SortedDictionary<string,List<string>>();
foreach(var f in Directory.GetFiles("src","*.cs",SearchOption.AllDirectories).Where(f=>!f.Contains("/obj/")))
foreach(var n in CSharpSyntaxTree.ParseText(File.ReadAllText(f)).GetRoot().DescendantNodes())
{
 string text=n switch{LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression)=>l.Token.ValueText,
 InterpolatedStringExpressionSyntax i=>string.Concat(i.Contents.Select((c,k)=>c is InterpolatedStringTextSyntax t?t.TextToken.ValueText:"{"+i.Contents.Take(k).Count(x=>x is InterpolationSyntax)+"}")),_=>""};
 if(!Regex.IsMatch(text,"[\\u4e00-\\u9fff]"))continue;
 if(!values.ContainsKey(text))values[text]=new();values[text].Add(f+":"+(n.GetLocation().GetLineSpan().StartLinePosition.Line+1));
}
File.WriteAllText("localization-source-catalog.json",JsonSerializer.Serialize(values,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
var translations = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText("src/XGLauncher/Localization/en-US.json"))!;
var missing = values.Keys.Where(x => !translations.ContainsKey(x)).ToArray();
foreach (var key in missing) Console.WriteLine("MISSING " + JsonSerializer.Serialize(key));
Console.WriteLine($"Owned source strings: {values.Count}; English resources: {translations.Count}; missing: {missing.Length}");
Environment.ExitCode = missing.Length == 0 ? 0 : 1;
