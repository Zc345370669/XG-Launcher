# PCL2（Plain Craft Launcher 2）目录架构参考鸣谢

XG Launcher 的 Minecraft 版本隔离目录组织参考了 PCL2（Plain Craft Launcher 2）的目录架构思路：按版本管理游戏配置、模组、光影、资源包、存档及日志，公共 libraries 和 assets 共用，系统 Java 由发行商安装器管理；不在版本目录中再嵌套一层 `versions`。

参考项目：https://github.com/Meloong-Git/PCL
原项目地址：https://github.com/Hex-Dragon/PCL2 （现重定向至上述仓库）

此项属于目录架构思路参考鸣谢。本次未复制、改编或分发 PCL2 源码、图标、界面资源或二进制；XG Launcher 的实现基于自身既有代码及 CmlLib 的公开扩展接口。此说明不是 PCL2 的许可证正文，也不声称 PCL2 采用 MIT 或已授权任何 PCL2 内容。

XG Launcher 自有代码继续采用原有 MIT 主许可证；现有第三方内容分别遵守其原许可证。本鸣谢不表示 PCL2 官方认可、合作或赞助。

XG Launcher 是独立项目。默认游戏根为 EXE 同级 `.minecraft`，版本隔离目录为 `.minecraft/versions/<版本>`，不创建 PCL2 命名的游戏数据目录。
