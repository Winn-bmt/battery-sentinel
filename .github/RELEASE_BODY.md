## 电量哨兵 v${{ env.VERSION }}

Windows 11 桌面小工具：监控 2.4G 无线鼠标 / 键盘 / 耳机接收器的电量，低电量时弹窗提醒。
绿色单文件，免安装、不联网、无第三方依赖。

### 下载

- `BatterySentinel-<version>-win64.zip`：程序 + 使用说明 + 截图
- `sha256.txt`：发布包校验值

### 运行要求

- Windows 10 / 11 x64（使用系统自带的 .NET Framework 4.8）
- 解压后双击 `BatterySentinel.exe` 即可

### 使用提示

- 建议先解压到本地文件夹，再从资源管理器双击运行（从压缩包预览/聊天窗口直接打开可能因权限受限导致设置或开机自启失败）
- 未签名版本的首次运行可能触发 SmartScreen：选择「更多信息 → 仍要运行」
- 配置与提醒状态保存在 `%APPDATA%\BatterySentinel`

完整版本变更见 [CHANGELOG.md](CHANGELOG.md)。
