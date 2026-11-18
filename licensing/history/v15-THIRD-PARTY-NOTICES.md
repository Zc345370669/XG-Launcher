# 第三方许可证与声明

XG Launcher 项目自有代码为 MIT，不对第三方依赖、Minecraft内容或商标重新授权。
当前全部锁定NuGet包、精确版本、包哈希与原文映射见 [依赖清单](licenses/DEPENDENCY-INVENTORY.md) 和 [机器可读记录](licenses/LOCKED-PACKAGE-LICENSES.json)。

LZMA-SDK 19.0.0 与 MicroCom.Runtime 0.11.0 的原始MIT版权声明已随附；需在分发相应组件时保留。LZMA-SDK包许可证不自动覆盖其他7-Zip/UnRAR组件。
Microsoft组件保留各自许可/NOTICE。已删除不在当前依赖图中的旧Microsoft.Extensions误归属文档，不影响业务模块。
NSIS 3.11 仅为构建工具，官方COPYING全文见 [NSIS](licenses/NSIS-COPYRIGHT.txt)；构建脚本要求该版本，安装器使用zlib。若升级构建工具或增加插件，必须重新核对对应授权。

本仓库没有分发游戏JAR、模组、Java运行时二进制、第三方字体或独立第三方项目图标图片文件。UI所用系统字体只引用名称。XG Logo为基线提供的资源，其权属证明仍待维护者确认，详见 [素材记录](licenses/ASSET-PROVENANCE.json)。未宣称所有素材已完全获准公开。

## 内联线性图标来源待核对

内联 Geometry 的来源尚未逐项确认，不能以没有独立图片文件断定不存在第三方图形表达。作为可能涉及的上游声明，补充 [Lucide 完整许可证](licenses/Lucide-LICENSE.txt)，包括 ISC 与 Feather 派生图标的 MIT 声明。固定官方版本与哈希见 licensing/ICON-PROVENANCE.json；这不是 Lucide 实际使用版本或每个图标来源的证明。声明完整性与来源确认分开记录。第三方文本不因目标 MIT 而改为 MIT。

## 本轮最终核对

既有全部原始 LICENSE/NOTICE 与 Lucide ISC、Feather MIT 全文保留；不将第三方图标许可作为项目主许可。内联几何的来源仍逐项待确认。正式 Logo 的来源已按维护者补充记录为 ChatGPT 直接生成的第一款锐利几何 XG 连体设计，不宣称独占权或商标清查通过。来源与授权边界见 licensing/FINAL-REVIEW.md。

原生库补充边界：SkiaSharp/HarfBuzzSharp 的 Windows NOTICE 包含 GIF 的 MPL/GPL/LGPL 可选条款、FreeType 的 FTL/GPL 选择及 libmicrohttpd 的 LGPL/eCos 条件。完整原文保留；不能只用包的顶层 MIT 代替。实际编译内容、采用的选项及源码/重新链接义务待核实，不据关键词认定整个应用自动变成 GPL-only，也不据此认定已无条件满足整体 MIT 发布的所有义务。FreeType 项目及其版权依原始随附 NOTICE 保留；本程序包含使用 FreeType 字体渲染技术的上游组件声明。

## v15 MIT 目标

维护者已撤回未来 XGCL 目标，计划在具备必要权利后采用标准 MIT。当前应用仍 GPL-3.0-only，第三方授权保持原样；完整 Lucide ISC 与 Feather MIT 不被覆盖。原生 DLL 继续按自身实际许可条件核对，独立依赖与复制进入应用的第三方源码不可混为一谈。

## v16 现行范围

本节为当前状态，前述旧版待核文字见历史记录，不作为当前主许可。应用自有代码采用 MIT，第三方不改授 MIT。Lucide 官方提交 de8d11aac831d519d2377ff516868244475fac06 的 33 个 SVG 逐文件记录在 licensing/LUCIDE-ASSETS.json；继续完整保留 ISC 和所使用 Feather 派生部分 MIT，不能删除 Feather 声明。

原生处理详见 licensing/NATIVE-COMPLIANCE.md。GIF 三重许可选择 MPL-1.1，完整 MPL、原始 GIF 声明及未修改的 DEPS 固定版本源码包位于 licenses/ 和 third-party/native-sources/，应随 Windows 发布一并保留。HarfBuzz 精确上游 COPYING 新增并与包原声明同时保留。未将这些源码/二进制或图标变成 XG Launcher 自有 MIT 内容。
