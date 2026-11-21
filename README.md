# PCL 验证补丁工具（PCLInjector）

一个 WinForms GUI 工具，对 .NET 单文件启动器执行**动态 IL 补丁**，解除"必须先验证正版账户才能添加其他账户类型/使用部分功能"的前置限制。

- 无需原始源码，直接对**你自己合法持有**的 exe 做符号级修改
- 只改方法体内部字节，**文件大小不变**
- 不写死文件偏移 / metadata token，按方法名与 IL 结构特征动态定位，可适应重新编译的版本
- 单文件发布，最终产物只有一个 `PCLInjector.exe`（约 1.4 MB）
- 编译好的 exe 已发布在 [Releases](https://github.com/aznb6666/PCLInjector/releases)，可直接下载使用

> **合规声明**：本工具仅用于对**你自己拥有合法副本的程序**进行二次开发/功能解锁。请遵守相应软件的许可证与当地法律，不要用于分发侵权产物。

---

## 目录

- [简单原理](#简单原理)
- [注入点清单](#注入点清单)
- [使用方法](#使用方法)
- [界面](#界面)
- [构建](#构建)
- [目录结构](#目录结构)
- [验证结果](#验证结果)

---

## 简单原理

目标程序是 **.NET 单文件（single-file）+ 框架依赖** 发布的 exe：它把自身的程序集（dll）以原始 PE 字节内嵌在 exe 尾部，运行时再解压加载。本工具利用这一点，做三步：

```
┌──────────────────────────────────────────────────────────────┐
│ 1. 扫描 bundle                                               │
│    逐字节搜索 "MZ" + "PE\0\0" 特征，找出 exe 内嵌的所有 PE     │
│    映像（约 93 个），得到每个 dll 的文件偏移与字节范围         │
└────────────────────────────┬─────────────────────────────────┘
                             ▼
┌──────────────────────────────────────────────────────────────┐
│ 2. 符号级定位（dnlib）                                        │
│    用 dnlib 加载每个内嵌程序集，按「方法名 + 返回类型 +        │
│    IL 操作码序列」匹配验证判定方法，不依赖固定偏移/token       │
└────────────────────────────┬─────────────────────────────────┘
                             ▼
┌──────────────────────────────────────────────────────────────┐
│ 3. 原地改写 IL                                                │
│    A) 布尔判定方法 → 恒返回 true/false（ldc.i4.1/0 + ret）    │
│    B) 抛异常拦截方法 → 直接 ret                                │
│    C) 黄条提示 → 改 brfalse.s 的 1 字节跳转目标                │
│    把新字节写回原文件偏移，保持文件大小不变                     │
└──────────────────────────────────────────────────────────────┘
```

**为什么要关心 `ret` 的位置**：早期版本把替换字节写成 `ldc.i4.1; ret; nop...`（`ret` 在前、后面用 nop 填充到原长度）。这在 dnlib 的文本解析下看似正确，但 CLR JIT 有硬性规则——**`ret` 之后不能有任何指令（包括 nop）**，否则点击"新建档案"时抛 `System.InvalidProgramException`。现在改为 `[压栈值] + nop 填充 + ret（最后一字节）`，已经过真实 CLR JIT 编译执行验证。

**黄条提示为何只改 1 字节**：在 `PageLoginProfile.RefreshProfileList` 中，提示的可见性由 `Profiles.Count == 0 ? Visible : Collapsed` 决定，对应 IL `brfalse.s 0x03`（跳到 `ldc.i4.0` = Visible = 显示）。把操作数改成 `0x00`，就会落到下一条 `ldc.i4.2` = Collapsed = 隐藏。只动 1 字节，不碰 try/catch 结构，比整体替换方法体安全得多。

### 目标 exe 的构建方式（分析结论）

对测试用的 `PCL2_CE_Release_x64.exe` 二进制分析：

| 项目 | 结论 |
|---|---|
| 架构 | x64（Machine 0x8664，PE32+） |
| 技术栈 | .NET / C#（元数据签名 BSJB，目标框架 net10.0） |
| 发布方式 | 单文件（single-file）+ 框架依赖（framework-dependent） |
| 打包特征 | 应用 dll 与 deps/runtimeconfig 以原始字节内嵌；运行时组件（coreclr/hostfxr）不内嵌 |
| 规模 | 主 exe 约 22.4 MB；内嵌约 93 个 PE 映像 |
| 程序集名 | `Plain Craft Launcher 2`（对应上游 PCL 主线结构，如 `PCL.ProfileUi`） |

---

## 注入点清单

工具按「方法名 + 返回类型 + IL 结构」动态定位以下 5 类判定（实测命中 6 处）：

| 方法 | 作用 | 补丁后 |
|---|---|---|
| `ProfileUi::CanCreateOtherProfile` | 是否允许添加第三方/离线账户 | 恒 `true` |
| `Lang::get_IsFeaturesUnrestricted` | 地区功能围栏 | 恒 `true` |
| `RestrictedFeaturePolicy::get_IsRestricted` | 受限功能判定 | 恒 `false` |
| `RestrictedFeaturePolicy::ThrowIfRestricted` | 受限功能拦截（抛异常） | 直接 `ret` |
| `PageLoginProfile::RefreshProfileList` | 档案列表为空时显示"需要先正版验证"黄条 | 隐藏黄条（分支重定向，1 字节） |

前 4 处是**方法体整体替换**（`ReturnValue` 规则），第 5 处是**分支重定向**（`BranchRetarget` 规则）。"动态查找"体现在不写死任何偏移和 token，每次运行重新扫描 bundle、按符号与结构特征定位，因而能适应重新编译版本（只要方法名与签名未变）。

---

## 使用方法

1. 从 [Releases](https://github.com/aznb6666/PCLInjector/releases) 下载 `PCLInjector.exe` 并运行（需系统安装 **.NET 10 桌面运行时**）。
2. 点击"**选择 exe**"，选中目标主程序（如 `PCL2_CE_Release_x64.exe`）。
3. 点击"**绕过正版验证**"，弹窗选择写入方式：
   - **备份原文件（推荐）**：先把原 exe 复制为同目录下的 `<原名>.exe.bak`（已存在则不覆盖），再写入补丁。出问题可用 `.bak` 还原。
   - **替换原文件**：直接覆盖原 exe，无备份（弹窗内含不可恢复的风险提示）。
   - **取消**：不执行任何操作。
4. 最终只保留一个 exe：注入版与原文件**同名**，直接写回所选路径，不另生成 `_injected.exe`。
5. 日志区显示扫描到的程序集数量、每个命中的方法与写入偏移。

---

## 界面

主界面与确认弹窗均为**响应式布局**，可自由拖拽缩放，无固定长宽比例。

- 路径输入框使用可换行的富文本框，长路径完整显示不截断
- 日志区占满剩余空间，文字带内边距，首字符不被裁切
- 弹窗中的路径与提示按宽度自动换行；窗口过窄时主体可滚动，三个按钮始终固定在底部
- 最小尺寸：主界面 640×460，确认弹窗 520×400
- 图标：蓝色圆角背景 + 白色盾牌 + 对勾，内嵌 7 个尺寸（16~256）

---

## 构建

### 单文件发布（推荐，与 PCL 官方同款）

```bash
bash scripts/publish-singlefile.sh [输出目录]   # 默认 publish/
```

- `-r win-x64 --self-contained false`：不打包 .NET 运行时，目标机需装 .NET 10 桌面运行时
- `PublishSingleFile=true` + `IncludeNativeLibrariesForSelfExtract=true`：dnlib.dll、deps/runtimeconfig 全部嵌入 exe
- 不启用压缩（framework-dependent + 压缩会触发 `NETSDK1176`），发布后删除 PDB

产物：运行脚本后生成 `publish/PCLInjector.exe`（目录内只有这一个 exe，无 dll/json/pdb 附带文件）。该目录已加入 `.gitignore`，正式发布产物上传至 Releases。

### 普通构建

```bash
dotnet build PCLInjector.csproj -c Release
dotnet publish PCLInjector.csproj -c Release -o publish
```

依赖：.NET 10 SDK、dnlib 4.4.0。

### 重新生成图标

```bash
python scripts/gen_icon.py     # 仅用 Python 标准库，生成 icon.ico 与 ui-preview/icon-preview.png
```

---

## 目录结构

```
PCLInjector/
├── PCLInjector.csproj        # 项目配置（含 ApplicationIcon 与单文件发布参数）
├── Program.cs                # 入口
├── MainForm.cs               # 主界面（响应式布局）
├── ConfirmDialog.cs          # "备份/替换/取消"确认弹窗
├── UiTheme.cs                # 渐变标题栏、圆角卡片等自定义控件
├── BundleParser.cs           # 扫描 exe 内嵌 PE 映像
├── PatcherEngine.cs          # 补丁规则与 IL 改写核心
├── icon.ico                  # 程序图标
├── scripts/
│   ├── publish-singlefile.sh # 单文件发布脚本
│   └── gen_icon.py           # 图标生成脚本
├── publish/                  # 本地构建输出（.gitignore 忽略，不入库）
│   └── PCLInjector.exe       # ← 由发布脚本生成；正式产物见 Releases
└── ui-preview/               # 界面与图标预览图
```

---

## 验证结果

- 实测命中 6 处，注入版大小与原版一致（23,486,024 字节）
- dnlib 重新加载验证 IL 已正确改写：
  - `CanCreateOtherProfile` → `ldc.i4.1 nop... ret`（恒 true）
  - `get_IsFeaturesUnrestricted` → `ldc.i4.1 nop... ret`
  - `get_IsRestricted` → `ldc.i4.0 nop... ret`
  - `ThrowIfRestricted` → `nop... ret`
  - `RefreshProfileList` → `IL_0066` 处 `2C 03` → `2C 00`
- 用真实 CLR JIT（Reflection.Emit）编译改写后的方法全部通过，无 `InvalidProgramException`
- 实际启动注入版走通"离线验证 → 继续 → 档案刷新"完整流程，黄条不再显示
- 单文件 exe 经 PE 资源段解析确认内嵌 7 个尺寸图标，GUI 启动与响应正常
