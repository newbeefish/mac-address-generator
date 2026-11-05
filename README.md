# MAC 地址生成器

一个简洁的 Windows 桌面工具，用于生成随机、本地管理的单播 MAC 地址，并自动写入选定的物理网卡使其生效。

> 请仅在你拥有或获授权管理的设备上使用。修改 MAC 地址可能违反网络使用政策，并会造成短暂断网。

## 功能

- 一键生成随机 MAC 地址（`02:xx:xx:xx:xx:xx` 风格的本地管理单播地址）。
- 自动把新地址写入所选物理网卡的 `NetworkAddress`，随后重启网卡使其生效。
- 顶部显示当前网卡实际地址。
- 每次生成前，自动把旧地址按时间存入历史记录。
- 目标网卡下拉选择，默认选择当前已连接的物理网卡。
- 白色简洁界面，支持双击复制当前地址。
- 安装到 `C:\MacAddressGenerator`，`config.json` 和 `history.json` 均保存在程序同目录。
- 安装器自动创建桌面快捷方式，并在完成后删除自身。

## 使用

1. 双击 `dist/MAC地址生成器_安装程序.exe`。
2. 允许管理员权限（安装和修改网卡都需要）。
3. 安装器会把程序放到 `C:\MacAddressGenerator`，创建桌面快捷方式并打开程序。
4. 选择目标网卡，点击“生成并自动写入网卡”。
5. 网卡会短暂断开再恢复，新地址会自动生效，旧地址会进入历史记录。

以后可以通过桌面的“MAC 地址生成器”快捷方式启动。启动时 Windows 会弹出 UAC 提示，这是修改网卡所必需的。

## 注意事项

- 只列出物理以太网 / Wi-Fi 网卡，VPN、虚拟机、Loopback 等虚拟网卡会被过滤。
- 极少数网卡驱动不支持 `NetworkAddress` 注册表项，写入会失败并给出提示。
- 修改 MAC 地址会短暂断网；如果正在使用远程连接，请谨慎操作。
- 程序不会联网，也不会上传任何配置或历史数据。

## 目录结构

```text
.
├─ assets/                       # 图标资源
├─ build/                        # 编译脚本（输出目录已被忽略）
├─ dist/                         # 已编译的安装程序
├─ src/
│  ├─ MacAddressGenerator/       # WinForms 主程序
│  └─ Installer/                 # 安装器
└─ README.md
```

## 构建

环境要求：Windows、.NET Framework 4.x、Windows PowerShell 5.1。

在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build\build.ps1
```

生成文件：

- `build/out/MacAddressGenerator.exe`
- `build/out/MAC地址生成器_安装程序.exe`
- `dist/MAC地址生成器_安装程序.exe`
