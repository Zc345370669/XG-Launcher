# Windows 原生组件许可处理

依据精确锁定 NuGet 包、nuspec 仓库提交及其 Skia 子模块/DEPS 对照，继续保留所有原始 NOTICE，且补足实际原生来源说明。

1. SkiaSharp 2.88.9：提交 9699cdf046a989423a5e5eca68e7fd813486d81c，Skia 子模块 4bed689c9c9eb77a120c6a9d54af6a572c85d1c2。cake/native-shared.cake 向构建追加 skia_enable_tools=false；libmicrohttpd 在 Skia BUILD.gn 的工具 skiaserve 依赖中，不是本次 libSkiaSharp 库目标的依赖。此前仅由广泛 NOTICE 推断其必定随库分发的疑点，不再作为实际 GPL/LGPL 冲突。
2. 默认 Windows Skia 配置的 skia_use_freetype 只对 Android/Fuchsia/Linux 为真；Windows 不是该默认路径。不删除已有 FreeType FTL/GPL 原文；如更换自定义构建启用它，须保留相应声明并按实际选择履约，而非把全部内容改称 MIT。
3. GIF 路径按 DEPS 指向 libgifcodec d06d2a6d42baf6c0c91cacc28df2542a911d05fe。采用其可选 MPL-1.1 路径，完整 MPL、原始版权/其他文件 BSD 声明及未修改源代码包一并提供，见 third-party/native-sources/。MPL 覆盖文件不改授 MIT；项目独立代码可以作为较大作品的 MIT 部分。
4. HarfBuzzSharp 8.3.1.1：包记录的 SkiaSharp 提交 64f24b1cddb68d30ec0ac6b661964178fc21d5ec，Skia 子模块 30abf4a0dcd3006f3a555e46906151e486f87b6e，DEPS 的 HarfBuzz 提交 2b3631a866b3077d9d675caa4ec9010b342b5a7c。保留该提交 COPYING 全文以及包原有声明。Windows vcxproj 以对应 HarfBuzz 源构建，没有以包顶层 MIT 省略原生版权。

本轮未发现强制把项目自有源码维持 GPL-only 的实际组件组合；按照已核对版本及此处分发材料处理旧疑点，不等于所有第三方作品都采用 MIT。发布者元数据与对应源码构建配置用于来源核对，未宣称逐字节复现上游 DLL；若未来换包、修改 GN 参数、压缩/裁剪组件或单独分发 EXE，必须重新检查对应义务。现行单文件会提取其所需本地库，第三方替换/调试权利不受项目 MIT 或品牌说明额外限制。
