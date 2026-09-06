# 竞速信息与反馈 V1 实施记录

## 已实现

- 首页集中说明比赛目标和最近选路观察，首次教学留在开跑前。
- 赛中左上角保留领先、终点距离和伤势；伤势显示当前值/上限，并区分“恢复中”“再受伤即出局”“已出局”。读取真实恢复时间和伤势上限，护盾与恢复保护规则不变。校准细项和长解释退出跑动主视区，当前门只保留一行预测。
- 地面路线补上预测矩形、反制三角、安全双横线，开跑前说明对应关系。符号共用现有材质、不参与碰撞，随赛道段回收；普通门与末门的放大符号均在本车道和障碍前方。符号占用原决策带与障碍之间的部分空地，不表示符号前缘与障碍仍相隔 12 米。
- 临时反馈共 2.2 秒：淡入 0.15 秒、停留 1.7 秒、淡出 0.35 秒。文字与底板共同淡出；事件按序号消费、优先级覆盖，不排队补播。暂停后丢弃旧事件，重开重新接收新事件。
- 目标车道、实际横向偏移和换道状态分别留证；选择锁定后不被后续改向覆写。碰撞、改路、未确认和取消分别记录，复盘分开说明关键选择和动作结果。
- 同门的通过结果和后续改猜合并为一句。没有确认通过时明确说未确认，不暗示竞速计分取消。
- UI 使用深灰、蓝白及少量橙色。主要按钮和关键变化使用橙色，普通强调使用蓝白。赛道原有路线颜色和游戏规则保持现有语义。

## 验证记录

| 验证层 | 结果 | 证据 |
| --- | --- | --- |
| 本轮 EditMode 全量回归 | 625 / 625 通过 | `TestResults/RacingFeedbackV1/FollowupChecks/EditMode.xml` |
| 本轮 PlayMode 全量回归 | 44 / 44 通过 | `TestResults/RacingFeedbackV1/FollowupChecks/PlayMode.xml` |
| 新增 UI 生命周期专测 | 2 / 2 通过，已包含在上述 44 项中 | `TestResults/RacingFeedbackV1/FollowupChecks/Lifecycle.xml` |
| 真实界面离屏截图 | 43 张：原有 22 张重渲，加 21 张伤势/无障碍图；三种视口 | `TestResults/RacingFeedbackV1/Captures` 与 `Captures/Followup` |
| 实际路线几何对照 | 彩色、统一灰白各 1 张，临时场景视角 | `TestResults/RacingFeedbackV1/Captures/Followup/route-symbols-fixture.txt` |
| 本轮 Windows 验证包 | 构建成功，与原可玩包分开存放 | `TestResults/RacingFeedbackV1/FollowupChecks/CaptureAndBuild-Final.log` |
| 本轮运行中的实际赛道 | 固定身份、种子 1337，在 100/120/135/150 米采集 4 帧实际摄像机与 HUD | `TestResults/RacingFeedbackV1/FollowupChecks/Player.log` |

首轮测试发现提示停用测试边界不正确，以及一个旧碰撞测试没有等待保护期结束；均已修复并在全量回归通过。实际 HUD 渲染另发现中文领先文字被高度裁掉，以及无 sprite 进度条显示满条；已修正并新增实际字形顶点、网格宽度测试。结算初建和响应式适配共用正文尺寸，避免适配时缩回旧高度。

首页教学的两行区域已扩高，路线形状说明完整显示。实际浅色赛道背景上的底板偏透，因此提高了底板不透明度。本轮全量回归覆盖该调整及新增伤势和形状；已查看大字、高对比、16:10 视口的实际 UI 渲染，新增伤势长句没有截断。

本轮新增生命周期测试使用实际 UIManager 和资源 HUD，调用真实暂停/继续按钮及 GameManager 状态事件；验证暂停期间发布的未刷新事件不补播、普通和减少动态效果模式均到期、终点结算清空提示、下一局序号 1 正常显示。反馈事件仍由测试注入，GameManager 不自动推进赛道，不等同于真实操作跑完比赛或场景重载。

首次扩展截图在未保存临时场景间使用 Additive 切换失败，未产生新包；失败日志保留为 `FollowupChecks/CaptureAndBuild.log`。修正自有临时场景清理后，重新完整执行截图与构建成功，证据以 `CaptureAndBuild-Final.log` 为准。

普通隐藏窗口截图返回纯黑，作为失败证据留在 `ScreenBufferBlackCaptures`，不计入视觉验收。新增显式 `-echo-qa-offscreen-capture` 诊断开关，使用实际比赛摄像机重渲染当前场景并临时绑定 HUD，采集后恢复相机与 Canvas 状态；默认启动不启用此分支。这些图片是运行场景的离屏复渲，不是原生窗口截图。

## 查看截图

- [新包实际路线形状与 HUD](../TestResults/RacingFeedbackV1/Windows/VisualCaptures/target-135m_actual-135p082m_1920x1080.png)
- [新包赛道中的伤势提示](../TestResults/RacingFeedbackV1/Windows/VisualCaptures/target-150m_actual-150p08m_1920x1080.png)
- [首次首页](../TestResults/RacingFeedbackV1/Captures/1920-menu-first-run.png)
- [挑战首页](../TestResults/RacingFeedbackV1/Captures/1920-menu-challenge.png)
- [提示淡入](../TestResults/RacingFeedbackV1/Captures/1920-fade-in.png)、[停留](../TestResults/RacingFeedbackV1/Captures/1920-hold.png)、[淡出](../TestResults/RacingFeedbackV1/Captures/1920-fade-out.png)、[消失](../TestResults/RacingFeedbackV1/Captures/1920-hidden.png)
- [校准进度](../TestResults/RacingFeedbackV1/Captures/1920-calibration.png)
- [结果与改猜合并](../TestResults/RacingFeedbackV1/Captures/1920-relearn-combined.png)
- [跑赢后的结算](../TestResults/RacingFeedbackV1/Captures/1920-result-promotion.png)、[反制路线碰撞后的结算](../TestResults/RacingFeedbackV1/Captures/1920-result-counter-collision.png)
- [大字和高对比](../TestResults/RacingFeedbackV1/Captures/Followup/1920-1080-large-text-high-contrast.png)
- [伤势恢复中](../TestResults/RacingFeedbackV1/Captures/Followup/1920-1080-injury-recovering.png)、[再受伤即出局](../TestResults/RacingFeedbackV1/Captures/Followup/1280-800-injury-next-hit-out.png)
- [路线彩色对照](../TestResults/RacingFeedbackV1/Captures/Followup/1920-route-symbols-fixture-color.png)、[同几何单色对照](../TestResults/RacingFeedbackV1/Captures/Followup/1920-route-symbols-fixture-monochrome.png)

## 验证边界

- HUD、首页与结算离屏截图使用真实 Unity UI 和注入状态，验证布局、字形和指定时刻的透明度，不代替完整比赛。
- 提示生命周期、暂停/恢复、终点结算、新局事件及计分和证据链分别有自动化证据。新增整合用例未覆盖物理碰撞致死后的完整场景重载；不能把终点结算测试写成已验证全部死亡/重开操作。
- 三种尺寸是离屏渲染视口，未执行原生游戏窗口拖拽缩放。Windows 图另外记录固定身份实际跑动中的显示；高速动态可读性仍待真人操作确认。
- [陌生玩家试玩流程及空表](2026-09-05-racing-feedback-v1-playtest.md)已准备，当前无受试者记录。两三人排错与六人验证尚未进行，不能据此宣布“玩家高速时不再漏看信息”。
- 本次没有实施两局约 90 秒、赛前立约、新诱因探测或回声提前押线。这些保持为后续独立设计。
- 工作区原有美术、动画、音效和动作反馈改动保留；没有提交或推送 Git。

## 复现入口

- 重建界面：团结编辑器执行 `EchoHudPrefabBuilder.Build`。
- 离屏截图：使用 GPU 批处理执行 `RacingFeedbackCapture.Capture`，不要使用 `-nographics`。
- 仅补充图：执行 `RacingFeedbackCapture.CaptureFollowup`；仅路线几何对照：执行 `RacingFeedbackCapture.CaptureRouteSymbols`。
- 独立验证包：执行 `RacingFeedbackCapture.BuildValidationPlayer`，输出 `TestResults/RacingFeedbackV1/Windows/EchoRun.exe`，不覆盖原可玩包。
- 同步截图与验证包：执行 `RacingFeedbackCapture.CaptureAndBuildValidationPlayer`。
- 测试与截图输出统一位于 `TestResults/RacingFeedbackV1`，本轮测试及构建日志在 `FollowupChecks`，此前日志保留。

本轮已查看 135 米和 150 米实际跑动帧：三种路线符号在赛道上渲染，左上角显示真实“伤势 1/2 · 再受伤即出局”。这是固定种子下未发送游戏输入的运行采样，未覆盖整个比赛。最后距离采集完成后，仅核对并关闭本轮创建的验证进程 PID 41292，已确认退出。

另以 1280×720 隐藏启动补充进程 PID 12152，不启用定距冻结。`FollowupChecks/Player-NativeLifecycle.log` 记录其进入自然死亡协程与隔离结算；但该进程没有可操作的主窗口（MainWindowHandle=0），未执行原生窗口缩放或重开按钮操作，也不算这两项验收通过。仅检查桌面、未向其他应用发送输入，随后核对路径并关闭该进程。

包体校验与验证清单见 `TestResults/RacingFeedbackV1/verification.json`。Windows 构建使用 IL2CPP，本轮运行代码变更体现在 `GameAssembly.dll`；没有内容变化的资源容器可沿用旧时间戳，不能据此把旧 exe 时间戳当作本次构建时间。

依据：[第一版方案](2026-09-05-racing-feedback-v1.md)。
