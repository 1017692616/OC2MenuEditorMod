# OC2 Menu Editor

独立的 Overcooked! 2 BepInEx mod，只保留原 OC2 Assist Mod 中的“设置菜单”和“前 N 道菜排除”功能。

- 游戏内按 `F6` 打开菜单，快捷键可在 BepInEx 配置中修改。
- 设置菜单按关卡保存 5 组预设；预设和前 N 道排除配置自动保存。
- 数量留空、非数字、负数按 `0` 处理。
- 前 N 道排除优先于关卡自带固定出菜顺序；被排除的固定菜会推进固定序列，随机菜单的累计权重不受拒绝抽样影响。
- F6 标题包含作者：连营&大幽灵&小松鼠 centoscj@gmail.com。

## 构建

```powershell
.\build.ps1 -NoInstall -NoLaunch
```

输出为 `dist/OC2MenuEditorMod.dll`。插件配置文件为：
`BepInEx/config/local.centos.overcooked2.menu-editor.cfg`。
