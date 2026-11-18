# 代码签名（SignPath Foundation，免费开源方案）

本项目通过 [SignPath Foundation](https://signpath.org/) 申请免费的 Authenticode 代码签名。
证书私钥保存在 SignPath 的 HSM 上，签名在 GitHub Actions 中完成，不需要自己购买证书或准备 USB Key。

> 现状：申请与配置流程已经写好（`.github/workflows/release.yml` + `.signpath/artifact-configuration.xml`），
> **在审核通过并配置仓库变量之前，workflow 会跳过签名，只产出未签名包**。

## 1. 申请

1. 打开 <https://signpath.org/apply.html> 填写申请表（HubSpot 表单）。
2. 表单里可以用下面这段（按需修改）：

```
Project name: 电量哨兵 (BatterySentinel)
Repository: https://github.com/Winn-bmt/battery-sentinel
License: MIT
Website / download page: https://github.com/Winn-bmt/battery-sentinel/releases
Description: Windows 11 tray utility that monitors the battery level of 2.4G wireless
  peripherals (mouse/keyboard/headset dongles, plus Bluetooth HFP devices) and shows
  low-battery notifications. Fully offline: no network access, no telemetry, no installer.
Build system: GitHub Actions, windows-latest, built from source with build.ps1
  (.NET Framework 4.8 + WPF, Roslyn compiler from the .NET SDK, no NuGet dependencies).
Artifact: BatterySentinel-<version>-win64.zip containing BatterySentinel.exe.
Maintainer: Winn-bmt
```

3. SignPath 会人工审核（项目是否开源、是否活跃、是否已有发布、是否能在 CI 中从源码构建）。
   审核期间可以正常发版，workflow 会自动跳过签名。

## 2. 审核通过后的配置

### 2.1 SignPath 控制台

在 SignPath 里确认/创建：

| 对象 | 建议值 | 说明 |
|---|---|---|
| Organization | 你的组织 | 记下 **Organization ID** |
| Project | `battery-sentinel` | 记下 project slug |
| Signing Policy | `release` | 只允许 release 分支/tag，记下 policy slug |
| Artifact Configuration | `release-zip` | 上传 [`.signpath/artifact-configuration.xml`](.signpath/artifact-configuration.xml)，记下 slug |

制品配置的含义：对 `BatterySentinel-<version>-win64.zip` 根目录下的 `BatterySentinel.exe`
做 Authenticode 签名，zip 里其它文件原样保留。

### 2.2 GitHub 仓库

1. **安装 SignPath GitHub App**：在 SignPath 的 GitHub 连接设置里安装，并授权 `battery-sentinel` 仓库
   （SignPath 需要读取 workflow 运行信息，OSS 项目要求所有 job 跑在 GitHub 托管 runner 上）。
2. **生成 API Token**：SignPath → 用户设置 → API token，权限选对应 project 的 submitter。
3. **配置仓库变量与密钥**（Settings → Secrets and variables → Actions）：

| 类型 | 名称 | 值 |
|---|---|---|
| Secret | `SIGNPATH_API_TOKEN` | 上一步的 API Token |
| Variable | `SIGNPATH_ORG_ID` | Organization ID |
| Variable | `SIGNPATH_PROJECT_SLUG` | `battery-sentinel` |
| Variable | `SIGNPATH_POLICY_SLUG` | `release` |
| Variable | `SIGNPATH_ARTIFACT_CONFIG_SLUG` | `release-zip` |

4. **启用自动发版**：编辑 `.github/workflows/release.yml`，把 `push: tags: ['v*']` 两行取消注释并提交。

## 3. 发版流程

```text
改版本号 → 提交 → 打 tag 并推送
  git tag v1.0.4
  git push origin v1.0.4
```

workflow 会自动：

1. `build.ps1` 编译出 `BatterySentinel.exe`
2. 打包 `BatterySentinel-1.0.4-win64.zip`
3. 上传为 GitHub Actions artifact
4. 提交 SignPath 签名请求（`signpath/github-action-submit-signing-request@v3`），等待完成
5. 用签名后的 zip 创建 GitHub Release，并附 `sha256.txt`

如果还没配置变量，第 4 步自动跳过，Release 里是未签名包。

## 4. 校验签名

在 Windows 上：

```powershell
Get-AuthenticodeSignature .\BatterySentinel.exe | Format-List Status, StatusMessage, SignerCertificate
# Status 应为 Valid
```

或用 Windows SDK 的 `signtool verify /pa /v BatterySentinel.exe`。

## 5. 常见问题

- **SmartScreen 仍提示？** 首次发布的新文件即使签名也可能短暂提示，等下载信誉建立后消失；
  签名保证的是“发布者身份 + 未被篡改”，不是立刻消除所有提示。
- **签名失败会不会卡住发版？** 当前 workflow 的签名步骤失败会让任务失败（不会静默发布未签名包），
  这样Release 里不会混入“看起来已签名其实没有”的文件；需要未签名发版时临时清空变量即可。
- **为什么不自己买证书？** 2023 年起代码签名私钥必须放硬件令牌/云 HSM，个人购买成本高；
  SignPath Foundation 对开源项目免费，且不需要暴露私钥。
