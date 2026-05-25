# 语义分块优化 - 快速测试指南

## 测试前准备

### 1. 确认环境
```bash
# 检查 Redis 是否运行
redis-cli PING
# 应返回: PONG

# 检查 Qdrant 是否运行
curl http://localhost:6333/collections
# 应返回集合列表

# 检查数据库连接
psql -U postgres -d knowledge_base -c "SELECT COUNT(*) FROM documents;"
```

### 2. 启动应用
```bash
cd KnowledgeBaseService.Api
dotnet build
dotnet run
```

---

## 测试场景

### 测试 1：基础功能测试

**目标**：验证优化后的分块器能正常工作

**步骤**：
1. 准备测试文本（500-1000字）
2. 调用分块接口
3. 检查返回的块数量和质量

**示例代码**：
```csharp
// 测试文本
var testText = @"
机器学习是人工智能的一个分支。它专注于让计算机从数据中学习。

深度学习是机器学习的子领域。深度学习使用神经网络模拟人脑。神经网络由多个神经元层组成。

Python是流行的编程语言。Python广泛应用于数据科学领域。NumPy是Python的科学计算库。
";

// 调用分块器
var (chunks, metrics) = await _semanticTextSplitter.SplitWithMetricsAsync(testText);

// 验证结果
Console.WriteLine($"块数量: {chunks.Count}");
Console.WriteLine($"碎片块: {metrics.FragmentedChunks}");
Console.WriteLine($"缓存命中率: {metrics.CacheHitRate:P2}");
Console.WriteLine(metrics.GenerateReport());
```

**预期结果**：
- ✅ 块数量：2-3 个
- ✅ 碎片块：0 个
- ✅ 每个块包含相关的句子

---

### 测试 2：缓存功能测试

**目标**：验证 Redis 缓存能正常工作

**步骤**：
1. 第一次分块（应该调用 Embedding API）
2. 第二次分块相同文本（应该从缓存读取）
3. 对比两次的缓存命中率

**示例代码**：
```csharp
var text = "测试文本...";

// 第一次：缓存未命中
var (chunks1, metrics1) = await splitter.SplitWithMetricsAsync(text);
Console.WriteLine($"第一次 - 缓存命中率: {metrics1.CacheHitRate:P2}");

// 第二次：缓存命中
var (chunks2, metrics2) = await splitter.SplitWithMetricsAsync(text);
Console.WriteLine($"第二次 - 缓存命中率: {metrics2.CacheHitRate:P2}");

// 验证
Assert.Equal(metrics1.TotalSentences, metrics2.CacheHits); // 第二次应该全部命中
```

**预期结果**：
- ✅ 第一次缓存命中率：0%
- ✅ 第二次缓存命中率：100%
- ✅ 第二次处理时间显著降低

---

### 测试 3：滑动窗口效果测试

**目标**：验证滑动窗口比相邻句比较更准确

**步骤**：
1. 准备包含"桥接句"的文本
2. 对比开启/关闭窗口的效果

**示例文本**：
```
机器学习算法可以分为监督学习和无监督学习。
监督学习需要标注数据。
分类问题是监督学习的典型应用。
常见的分类算法包括决策树和神经网络。
```

**测试代码**：
```csharp
var options1 = new SemanticChunkingOptions { WindowSize = 1 }; // 相邻句（旧逻辑）
var options2 = new SemanticChunkingOptions { WindowSize = 3 }; // 滑动窗口（新逻辑）

var (chunks1, _) = await splitter.SplitWithMetricsAsync(text, options1);
var (chunks2, _) = await splitter.SplitWithMetricsAsync(text, options2);

Console.WriteLine($"窗口=1: {chunks1.Count} 个块");
Console.WriteLine($"窗口=3: {chunks2.Count} 个块");
```

**预期结果**：
- ✅ 窗口=1：可能产生 3-4 个块（误判）
- ✅ 窗口=3：应该产生 1-2 个块（正确）

---

### 测试 4：段落感知测试

**目标**：验证段落感知功能

**步骤**：
1. 准备多段落文本
2. 验证段落不会被错误合并

**示例文本**：
```
机器学习是AI的分支。

深度学习使用神经网络。

Python是流行的语言。
```

**测试代码**：
```csharp
var options1 = new SemanticChunkingOptions { EnableParagraphAware = false };
var options2 = new SemanticChunkingOptions { EnableParagraphAware = true };

var (chunks1, _) = await splitter.SplitWithMetricsAsync(text, options1);
var (chunks2, _) = await splitter.SplitWithMetricsAsync(text, options2);

Console.WriteLine($"无段落感知: {chunks1.Count} 个块");
Console.WriteLine($"段落感知: {chunks2.Count} 个块");
```

**预期结果**：
- ✅ 段落感知：每个段落独立处理
- ✅ 不会跨段落合并

---

### 测试 5：质量指标验证

**目标**：验证质量指标能正确反映分块效果

**步骤**：
1. 执行分块
2. 检查所有质量指标
3. 识别潜在问题

**测试代码**：
```csharp
var (chunks, metrics) = await splitter.SplitWithMetricsAsync(longText);

// 打印完整报告
Console.WriteLine(metrics.GenerateReport());

// 验证关键指标
Assert.True(metrics.FragmentedChunks < metrics.TotalChunks * 0.1, "碎片块应少于10%");
Assert.True(metrics.CacheHitRate > 0.5, "缓存命中率应>50%（重复文本）");
Assert.True(metrics.StdDevChunkSize < 500, "块大小标准差应<500");
```

**预期结果**：
- ✅ 所有指标都有合理值
- ✅ 能识别出质量问题（如碎片块过多）

---

## 性能基准测试

### 测试 6：大文档性能测试

**目标**：验证大文档（5000+字）的处理性能

**步骤**：
1. 准备 5000 字的测试文本
2. 测量处理时间
3. 对比优化前后的性能

**测试代码**：
```csharp
var longText = LoadTestDocument(5000); // 加载5000字文档

var stopwatch = Stopwatch.StartNew();
var (chunks, metrics) = await splitter.SplitWithMetricsAsync(longText);
stopwatch.Stop();

Console.WriteLine($"文档长度: {longText.Length} 字符");
Console.WriteLine($"块数量: {chunks.Count}");
Console.WriteLine($"处理时间: {stopwatch.ElapsedMilliseconds}ms");
Console.WriteLine($"平均块大小: {metrics.AverageChunkSize:F0} 字符");
Console.WriteLine($"Embedding 时间: {metrics.EmbeddingTimeMs}ms");
Console.WriteLine($"合并时间: {metrics.MergingTimeMs}ms");
```

**预期结果**：
- ✅ 处理时间 < 3秒（首次）
- ✅ 处理时间 < 500ms（缓存命中）
- ✅ 没有碎片块

---

## 实际场景测试

### 测试 7：真实文档测试

**目标**：使用真实文档验证效果

**步骤**：
1. 上传真实文档（PDF、Word、Markdown）
2. 通过 API 导入文档
3. 检查分块质量
4. 执行检索测试

**测试 API**：
```bash
# 1. 上传文档
curl -X POST http://localhost:5000/api/documents/upload \
  -F "file=@test.pdf" \
  -F "title=测试文档" \
  -F "category=技术"

# 2. 检索测试
curl -X POST http://localhost:5000/api/rag/query \
  -H "Content-Type: application/json" \
  -d '{
    "question": "文档中关于深度学习的介绍",
    "topK": 5,
    "enableHybridSearch": true
  }'
```

**验证点**：
- ✅ 文档成功分块
- ✅ 检索结果准确
- ✅ 来源引用正确

---

## 故障恢复测试

### 测试 8：故障场景测试

**目标**：验证系统的容错能力

**场景 1：Embedding API 失败**
```bash
# 停止 Embedding 服务
# 预期：自动回退到字符分块
# 验证：文档仍然能被索引
```

**场景 2：Redis 连接失败**
```bash
# 停止 Redis
# 预期：继续工作，只是没有缓存
# 验证：第一次处理时间变长，但结果正确
```

**场景 3：Qdrant 连接失败**
```bash
# 停止 Qdrant
# 预期：返回错误信息
# 验证：不会崩溃，日志中有错误信息
```

---

## 负载测试

### 测试 9：并发测试

**目标**：验证系统能处理并发请求

**步骤**：
```bash
# 使用 Apache Bench 或类似工具
ab -n 100 -c 10 http://localhost:5000/api/rag/query

# 或使用 PowerShell
1..10 | ForEach-Object {
    Start-Job -ScriptBlock {
        Invoke-RestMethod -Uri "http://localhost:5000/api/rag/query" `
          -Method POST -Body '{"question":"测试"}'
    }
}
```

**预期结果**：
- ✅ 没有崩溃
- ✅ 响应时间合理（< 5秒）
- ✅ 没有数据竞争

---

## 测试检查清单

- [ ] **基础功能**
  - [ ] 能正常分块文本
  - [ ] 返回合理数量的块
  - [ ] 块内容语义相关

- [ ] **缓存功能**
  - [ ] 首次调用缓存命中率为 0%
  - [ ] 第二次调用缓存命中率为 100%
  - [ ] 缓存失效后重新生成

- [ ] **质量指标**
  - [ ] 碎片块比例 < 10%
  - [ ] 块大小标准差合理
  - [ ] 平均相似度在预期范围

- [ ] **性能**
  - [ ] 小文档（<1000字）< 1秒
  - [ ] 中文档（1000-5000字）< 3秒
  - [ ] 大文档（>5000字）< 10秒
  - [ ] 缓存命中时 < 500ms

- [ ] **容错**
  - [ ] Embedding API 失败时回退到字符分块
  - [ ] Redis 不可用时继续工作
  - [ ] 异常情况不崩溃

- [ ] **实际场景**
  - [ ] 真实文档能正确索引
  - [ ] 检索结果准确
  - [ ] 来源引用正确

---

## 测试报告模板

```markdown
# 语义分块优化测试报告

**测试日期**：2025-12-31
**测试人员**：[您的名字]
**环境**：开发/测试/生产

## 测试结果摘要

| 测试项 | 状态 | 备注 |
|--------|------|------|
| 基础功能 | ✅ 通过 | - |
| 缓存功能 | ✅ 通过 | 命中率 100% |
| 滑动窗口 | ✅ 通过 | 窗口=3 效果更好 |
| 段落感知 | ✅ 通过 | 段落独立处理 |
| 质量指标 | ✅ 通过 | 碎片块 0% |
| 性能测试 | ✅ 通过 | 5000字文档 2.1秒 |
| 故障恢复 | ✅ 通过 | 回退机制正常 |
| 并发测试 | ⚠️ 通过 | 10并发时响应变慢 |

## 性能数据

- 平均处理时间：1200ms
- 缓存命中率：95%
- 碎片块比例：2%
- 平均块大小：850 字符

## 发现的问题

1. [问题1描述]
   - 严重程度：高/中/低
   - 建议修复：...

2. [问题2描述]
   - 严重程度：高/中/低
   - 建议修复：...

## 总体评价

✅ 优化效果显著，建议部署到生产环境

⚠️ 发现少量问题，修复后可部署

❌ 存在严重问题，需要重新优化
```

---

## 下一步

完成测试后：

1. **分析结果**：识别需要进一步优化的地方
2. **调整参数**：根据实际效果微调配置
3. **准备部署**：更新文档和监控
4. **持续监控**：收集生产环境指标

---

**祝测试顺利！** 🚀
