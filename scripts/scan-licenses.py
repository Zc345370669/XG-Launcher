"""Inventory the locked dependency versions and their restored NuGet metadata."""
import argparse
import html
import json
from pathlib import Path
import re
import shutil
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument("--packages", type=Path, default=Path.home() / ".nuget" / "packages")
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
licenses = root / "licenses"
packages = {}
for lock in sorted(root.glob("*/*/packages*.lock.json")):
    for graph in json.loads(lock.read_text()).get("dependencies", {}).values():
        for name, entry in graph.items():
            if "resolved" in entry:
                packages.setdefault((name, entry["resolved"]), set()).add(lock.relative_to(root).as_posix())

runtime = "10.0.15"
for name in ("Microsoft.NETCore.App.Runtime.win-x64", "Microsoft.NETCore.App.Host.win-x64"):
    if (args.packages / name.lower() / runtime).is_dir():
        packages[name, runtime] = {"Windows x64 self-contained host/runtime"}

def cell(value):
    return value.replace("|", r"\|").replace("\n", " ")

rows = []
missing = []
for (name, version), graphs in sorted(packages.items()):
    folder = args.packages / name.lower() / version
    metadata = list(folder.glob("*.nuspec"))
    if not metadata:
        missing.append(f"{name} {version}")
        continue
    nuspec = ET.parse(metadata[0]).getroot().find("{*}metadata")
    def value(key):
        return (nuspec.findtext("{*}" + key) or "").strip()
    license_element = nuspec.find("{*}license")
    declaration = value("license") or value("licenseUrl") or "未在包元数据声明"
    if license_element is not None and license_element.get("type") == "file":
        declaration = f"文件声明（{declaration}）"
    if name == "HtmlAgilityPack":
        declaration = "MIT（上游完整许可文本）"
    if name == "System.Security.Cryptography.ProtectedData":
        declaration = "MIT（上游完整许可文本）"
    files = set()
    for asset in folder.iterdir():
        if asset.is_file() and re.match(r"(licen[cs]e|third.party.notices|notice|copyright)(\.|$)", asset.name, re.I):
            target = licenses / f"{name}-{version}-{asset.name}"
            shutil.copyfile(asset, target)
            files.add(target.name)
    files.update(f.name for f in licenses.iterdir() if f.is_file() and f.name.startswith(name + "-") and
        any(word in f.name.lower() for word in ("license","notice")) and not f.name.startswith(name + ".")) 
    if name.startswith("Avalonia") and declaration == "MIT":
        files.add("Avalonia-LICENSE.txt")
    if name.startswith("Microsoft.NETCore.App"):
        files.update(["dotnet-10.0.15-LICENSE.TXT", "dotnet-10.0.15-THIRD-PARTY-NOTICES.TXT"])
    if not files:
        missing.append(f"{name} {version}: original license text not available; package declaration is not a substitute")
    links = ", ".join(f"[{x}]({x})" for x in sorted(files)) or "见对应 NuGet/上游"
    url = value("projectUrl") or f"https://www.nuget.org/packages/{name}/{version}"
    owner = value("copyright") or f"包作者：{value('authors')}"
    rows.append(f"| {cell(name)} | {cell(version)} | {cell(owner)} | {cell(declaration)} | [项目]({url}) | {links} | {cell(', '.join(sorted(graphs)))} |")

header = """# 第十三阶段依赖与许可清单

按源码锁文件及当前还原的 Windows x64 运行时生成。包括开发、测试及其他平台的依赖，图列说明来源。版权取自 NuGet 元数据及分发文本。不生成通用许可条文，不替换作者版权。缺少原文时明确记录为未核实；许可正文与来源证明独立保留。

| 名称 | 版本 | 版权或包作者 | 许可声明 | 官方项目地址 | 完整许可或通知 | 依赖图 |
|---|---|---|---|---|---|---|
"""
result = header + "\n".join(rows) + "\n"
if missing:
    result += "\n缺少还原后的包元数据：" + "、".join(missing) + "。\n"
(licenses / "DEPENDENCY-INVENTORY.md").write_text(result, encoding="utf-8")
print(f"Recorded {len(rows)} package versions. Missing restored package metadata: {', '.join(missing) or 'none'}")
