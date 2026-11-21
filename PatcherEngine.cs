using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace PCLInjector;

/// <summary>补丁引擎：在单文件 exe 中动态定位内嵌程序集，按"方法名 + 返回类型 + IL 结构特征"定位验证判定方法，改写其 IL 使判定反转。</summary>
public static class PatcherEngine
{
    public enum PatchKind
    {
        MethodReturn,     // 整体替换方法体为常量返回（bool getter / void 方法）
        BranchRetarget    // 在指定方法内按操作码序列定位分支，改写其跳转操作数
    }

    public sealed class PatchRule
    {
        public PatchKind Kind { get; init; } = PatchKind.MethodReturn;

        // —— MethodReturn 用 ——
        public required string MethodName { get; init; }
        public required string ReturnType { get; init; }   // "Boolean" / "Void"
        public required string FriendlyName { get; init; }
        // 返回值：true=压 ldc.i4.1；false=压 ldc.i4.0；null=void 方法不压栈
        public bool? ReturnValue { get; init; } = true;
        // 动态结构特征：方法体 IL 中必须出现的操作码序列（宽松匹配，忽略 token）
        public OpCode[]? StructureSignature { get; init; }

        // —— BranchRetarget 用 ——
        // 所在方法名（在该方法体内按 BranchSignature 定位）
        public string? ContainingMethod { get; init; }
        // 要匹配的操作码序列（忽略操作数 token）
        public OpCode[]? BranchSignature { get; init; }
        // 改写目标指令的最后一个字节（操作数）为该值
        public byte NewOperand { get; init; }
        public string? TargetType { get; init; }   // 可选：限定类型全名
    }

    public sealed class Hit
    {
        public string Assembly { get; set; } = "";
        public string Type { get; set; } = "";
        public string Method { get; set; } = "";
        public long FileOffset { get; set; }
        public int CodeSize { get; set; }
        public string FriendlyName { get; set; } = "";
    }

    public sealed class Result
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public byte[]? Image { get; set; }
        public List<Hit> Hits { get; } = [];
        public StringBuilder Log { get; } = new();
    }

    // 目标规则：改为"返回 true / 不受限 / 直接 return"
    // 关键：CLR 要求方法体最后一条指令必须是 ret/throw/br 等终止指令，
    //       ret 之后不能再有任何指令（含 nop），否则 JIT 抛 InvalidProgramException。
    // 因此布局为：[压栈值(可选)] + nop 填充 + ret(最后一字节)。
    private const byte OpLdcI4_1 = 0x17;   // ldc.i4.1 -> true
    private const byte OpLdcI4_0 = 0x16;   // ldc.i4.0 -> false
    private const byte OpNop = 0x00;
    private const byte OpRet = 0x2A;

    public static readonly List<PatchRule> Rules =
    [
        new() {
            MethodName = "CanCreateOtherProfile", ReturnType = "Boolean",
            FriendlyName = "允许添加第三方/离线账户", ReturnValue = true
        },
        new() {
            MethodName = "get_IsFeaturesUnrestricted", ReturnType = "Boolean",
            FriendlyName = "解除地区限制（功能不受限）", ReturnValue = true
        },
        new() {
            MethodName = "get_IsRestricted", ReturnType = "Boolean",
            FriendlyName = "受限策略判定关闭", ReturnValue = false
        },
        new() {
            MethodName = "ThrowIfRestricted", ReturnType = "Void",
            FriendlyName = "受限功能拦截移除", ReturnValue = null
        },
        // 隐藏登录页"需要先正版验证"黄条：PageLoginProfile.RefreshProfileList 中
        // ldfld HintMicrosoft → get_Profiles → get_Count → brfalse.s(显示) / ldc.i4.2(隐藏)
        // 把 brfalse.s 的跳转目标从"显示(ldc.i4.0)"改成"隐藏(ldc.i4.2)"，仅改 1 字节操作数
        new() {
            Kind = PatchKind.BranchRetarget,
            MethodName = "RefreshProfileList", ReturnType = "Void",
            ContainingMethod = "RefreshProfileList",
            TargetType = "PCL.PageLoginProfile",
            FriendlyName = "隐藏\"需要先正版验证\"黄条提示",
            BranchSignature = [
                OpCodes.Ldarg_0,
                OpCodes.Ldfld,          // HintMicrosoft
                OpCodes.Call,           // get_Profiles
                OpCodes.Callvirt,       // get_Count
                OpCodes.Brfalse_S,      // ← 目标指令（改它的操作数）
                OpCodes.Ldc_I4_2,
                OpCodes.Br_S,
                OpCodes.Ldc_I4_0,
                OpCodes.Callvirt        // set_Visibility
            ],
            NewOperand = 0x00           // brfalse.s 操作数改为 0 → 跳到下一条 ldc.i4.2(Collapsed) 隐藏
        },
    ];

    /// <summary>主入口：对 exe 字节执行动态查找与补丁。</summary>
    public static Result Patch(byte[] exeImage, IProgress<string>? progress)
    {
        var r = new Result();
        void L(string s) { r.Log.AppendLine(s); progress?.Report(s); }

        L("[1/4] 扫描单文件 bundle 中的内嵌程序集...");
        var images = BundleParser.Scan(exeImage);
        L($"      发现 {images.Count} 个内嵌 PE 映像");

        var output = (byte[])exeImage.Clone();

        // 全局去重：同一方法可能因内嵌镜像重叠被扫描多次
        var patchedKeys = new HashSet<string>(StringComparer.Ordinal);
        var matchedRules = new HashSet<string>(StringComparer.Ordinal);

        L("[2/4] 按方法名 + 返回类型 + 结构特征动态定位注入点...");
        foreach (var img in images)
        {
            ModuleDefMD? mod = null;
            try
            {
                mod = ModuleDefMD.Load(img.Data, ModuleDef.CreateModuleContext());
            }
            catch
            {
                continue;
            }

            try
            {
                foreach (var type in mod.GetTypes())
                {
                    foreach (var m in type.Methods)
                    {
                        foreach (var rule in Rules)
                        {
                            if (m.Name != rule.MethodName) continue;
                            if (!m.HasBody || m.RVA == 0) continue;

                            var retName = m.ReturnType.TypeName;
                            if (!string.Equals(retName, rule.ReturnType, StringComparison.OrdinalIgnoreCase))
                                continue;

                            // 二次校验：若定义了结构特征，则要求 IL 操作码前缀匹配（忽略操作数 token）
                            if (rule.StructureSignature is { Length: > 0 } sig)
                            {
                                var ops = m.Body.Instructions.Select(i => i.OpCode).Take(sig.Length).ToArray();
                                if (ops.Length < sig.Length) continue;
                                bool same = true;
                                for (int k = 0; k < sig.Length; k++)
                                    if (ops[k].Code != sig[k].Code) { same = false; break; }
                                if (!same) continue;
                            }

                            uint dllFileOffset;
                            try { dllFileOffset = (uint)mod.Metadata.PEImage.ToFileOffset(m.RVA); }
                            catch { continue; }

                            long exeOffset = img.FileOffset + dllFileOffset;
                            string key = $"{exeOffset}:{rule.MethodName}";
                            if (!patchedKeys.Add(key)) continue;

                            // 读取 tiny/fat header，得到 IL 代码起点与长度
                            if (exeOffset + 1 > output.Length) continue;
                            byte b0 = output[(int)exeOffset];
                            int ilStart, codeSize;
                            if ((b0 & 0x03) == 0x02) // tiny
                            {
                                ilStart = 1;
                                codeSize = b0 >> 2;
                            }
                            else if ((b0 & 0x03) == 0x03) // fat
                            {
                                if (exeOffset + 12 > output.Length) continue;
                                int headerDwords = (b0 >> 4) & 0x0F;
                                ilStart = headerDwords * 4;
                                if (ilStart < 12) ilStart = 12;
                                codeSize = BitConverter.ToInt32(output, (int)exeOffset + 4);
                            }
                            else continue;

                            if (codeSize <= 0) continue;
                            long ilAbs = exeOffset + ilStart;
                            if (ilAbs + codeSize > output.Length) continue;

                            long patchOffset;   // 实际写入字节的位置
                            int patchLen;       // 本次改写的字节数（用于日志）

                            if (rule.Kind == PatchKind.BranchRetarget)
                            {
                                // —— 分支重定向：在方法体内匹配操作码序列，改写目标指令的操作数 ——
                                if (rule.TargetType is { } tt && type.FullName != tt) continue;
                                var branchSig = rule.BranchSignature;
                                if (branchSig is not { Length: > 0 }) continue;

                                var instrs = m.Body.Instructions;
                                var ops = instrs.Select(i => i.OpCode).ToArray();
                                int found = -1;
                                for (int s = 0; s + branchSig.Length <= ops.Length; s++)
                                {
                                    bool same = true;
                                    for (int k = 0; k < branchSig.Length; k++)
                                        if (ops[s + k].Code != branchSig[k].Code) { same = false; break; }
                                    if (same) { found = s; break; }
                                }
                                if (found < 0) continue;

                                // 找到签名中 Brfalse_S 那条指令（要改操作数）
                                int bi = -1;
                                for (int k = 0; k < branchSig.Length; k++)
                                    if (branchSig[k].Code == OpCodes.Brfalse_S.Code) { bi = k; break; }
                                if (bi < 0) continue;

                                var target = instrs[found + bi];
                                // brfalse.s = 0x2C + 1 字节操作数，操作数在 Offset+1
                                long operandAbs = ilAbs + target.Offset + 1;
                                if (operandAbs >= output.Length) continue;
                                output[(int)operandAbs] = rule.NewOperand;
                                patchOffset = operandAbs;
                                patchLen = 1;
                            }
                            else
                            {
                                // —— 方法体整体替换（保持大小不变，ret 必须在最后一条）——
                                //   Boolean: [ldc.i4.0/1] + nop*(codeSize-2) + ret
                                //   Void   : nop*(codeSize-1) + ret
                                // 注意：绝不能让 ret 后面还有任何指令（含 nop），否则 JIT 抛 InvalidProgramException。
                                if (codeSize < 2) continue;
                                Buffer.BlockCopy(new byte[codeSize], 0, output, (int)ilAbs, codeSize);
                                if (rule.ReturnValue is bool rv)
                                    output[(int)ilAbs] = rv ? OpLdcI4_1 : OpLdcI4_0;
                                output[(int)(ilAbs + codeSize - 1)] = OpRet;
                                patchOffset = ilAbs;
                                patchLen = codeSize;
                            }

                            var hit = new Hit
                            {
                                Assembly = mod.Assembly?.Name?.String ?? mod.Name?.String ?? "?",
                                Type = type.FullName,
                                Method = m.Name,
                                FileOffset = patchOffset,
                                CodeSize = patchLen,
                                FriendlyName = rule.FriendlyName
                            };
                            r.Hits.Add(hit);
                            matchedRules.Add(rule.MethodName);
                            L($"      ✔ {hit.FriendlyName}");
                            L($"        {hit.Type}::{hit.Method}  @0x{hit.FileOffset:X} (改写 {patchLen}B)");
                        }
                    }
                }
            }
            finally
            {
                mod.Dispose();
            }
        }

        L("[3/4] 完整性检查...");
        if (r.Hits.Count == 0)
        {
            r.Message = "未找到任何注入点。可能原因：exe 不是目标程序、已加密/混淆，或版本验证实现不同。";
            L("      " + r.Message);
            return r;
        }

        var missing = Rules.Where(x => !matchedRules.Contains(x.MethodName)).Select(x => x.MethodName).ToList();
        if (missing.Count > 0)
            L($"      提示：以下注入点未命中（可能该版本不含）：{string.Join(", ", missing)}");

        L("[4/4] 生成已注入镜像...");
        r.Image = output;
        r.Success = true;
        r.Message = $"完成，共应用 {r.Hits.Count} 处补丁。";
        L("      " + r.Message);
        return r;
    }
}
