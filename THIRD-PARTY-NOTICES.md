# 第三方许可证与声明（v16）

XG Launcher 项目自有代码/文档采用 MIT，不对第三方依赖、图标、原生源码、游戏内容或商标重新授权。

全部锁定 NuGet 名称、版本、原文映射仍见 licenses/LOCKED-PACKAGE-LICENSES.json 与 DEPENDENCY-INVENTORY.md。保留所有原始版权、LICENSE/NOTICE，未以项目 MIT 省略第三方条款。

## Lucide / Feather

使用官方仓库固定提交 de8d11aac831d519d2377ff516868244475fac06 的 34 个 SVG，源码与每文件 SHA256 在 licensing/LUCIDE-ASSETS.json。原文在 licenses/Lucide-LICENSE.txt：完整 ISC，以及所用 Feather 派生图标的 MIT、版权和图标清单都保留。图标不是 XG Launcher 自有 MIT 代码；不能删除 Feather 声明。应用原十二内联几何已被替换，不靠外观猜测来源。

## 原生 Skia/HarfBuzz 与 GIF

Windows 原生组件分发依据见 licensing/NATIVE-COMPLIANCE.md。已按对应源码构建配置缩小工具依赖/默认字体路径范围。GIF 可选路径采用 MPL-1.1，完整原文与原始版权在 licenses/MPL-1.1.txt、libgifcodec-LICENSE.md；未修改对应源码归档在 third-party/native-sources/，随 Windows 发布提供。第三方 GIF 不改授 MIT，归档内部其他 BSD/Apple 声明也保留。

HarfBuzz 原生 COPYING见 licenses/HarfBuzz-upstream-COPYING.txt，对应 package -> Skia gitlink -> DEPS 原生提交记录。与包原始 Microsoft/HarfBuzzSharp 声明同时提供，不互相替代。若未来更改原生构建，应重新核查实际子组件和义务，不以顶层 MIT 概括全部。

## 其他依赖和资源

Avalonia/CmlLib/MicroCom/Microsoft 及 .NET runtime 的原文保留。LZMA-SDK 19.0.0 只按该 .NET 包许可核对，不推广为全部 7-Zip/UnRAR 的统一许可。ANGLE/Skia/HarfBuzz 各自 NOTICE 不删减。FreeType 原文及 FTL/GPL 选择说明保持；本次原生处理不强制选择 GPL。

NSIS 3.11 的官方 COPYING、固定版本及 zlib 构建设置保留；本轮只构建便携 EXE，未执行 NSIS。换压缩器/插件须另核对。

XG 正式 Logo 的 ChatGPT 来源和原文件保持，不保证独占商标/版权。玩家头像与远程项目封面属于内容数据，并非本程序自管功能图标，不被换成 Lucide或声明为自有 MIT。系统字体只引用名称，没有再分发字体文件。

历史 GPL 等授权不追溯撤销；当前第三方原始文本不因旧目标 XGCL 或新项目 MIT 而更名。请在分发时保留许可证目录和配套 third-party/ 材料，不只单独提供项目 LICENSE。
