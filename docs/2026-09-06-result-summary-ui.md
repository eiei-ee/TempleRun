# 结算页信息精简

针对实际胜利截图中的重复标题、红色描边和密集复盘，默认结算页只保留胜负、得分与金币、回声记录变化，以及下一局入口。

“查看本局复盘”展开原有证据，包括反制次数、后续预测调整和最近一次选路的动作结果。展开区域可以滚动，支持大字；再次点击收起，每次新的结算都默认收起。详情不再重复页面标题，“关键选择”改成与实际数据来源一致的“最近一次选路”。

保存失败、未形成新回声、固定验收不写身份档等结果仍在默认页可见。原始结果和存档未改写，选路判定、计分及障碍回收规则不在本次修改范围内。

## 验证入口

- 摘要与详情文案：`SingleContractResultPresentationTests`。
- 实际 UI 按钮、下一次结算重置和大字滚动：`RacingFeedbackUiLifecycleTests`。
- 独立截图：`RacingFeedbackCapture.CaptureResultSummary`。
- 截图并构建独立 Windows 包：`RacingFeedbackCapture.CaptureAndBuildResultSummaryPlayer`。
- 输出位于 `TestResults/ResultSummaryV1`；原来的 `RacingFeedbackV1` 玩家包保留。

截图使用真实 UI 和注入结算状态，区分界面验证与实际玩家比赛。最终结果以此目录下的测试 XML、构建日志和验证清单为准。

## 本次验证结果

- EditMode：658/658 通过，包括新增的 33 项摘要与详情文案用例。
- PlayMode：45/45 通过，包括真实结算按钮展开、收起、大字高度、下一次结算重置与旧模式兼容。
- Windows 独立包构建成功，日志未发现 C# 编译错误或异常。
- 生成 20 张真实 UI 离屏验证图，覆盖 1920×1080、1280×720、1280×800 的普通字与大字，以及保存失败和观察不足状态。

[打开新版游戏](../TestResults/ResultSummaryV1/Windows/EchoRun.exe) · [默认结算页](../TestResults/ResultSummaryV1/Captures/1920-1080-normal-promotion-summary.png) · [展开复盘（大字）](../TestResults/ResultSummaryV1/Captures/1280-720-large-promotion-details.png) · [验证清单与文件哈希](../TestResults/ResultSummaryV1/verification.json)

本次没有启动新版独立玩家，也没有复现原截图中的整场比赛。离屏图展示真实界面组件与注入的状态，不包含比赛背景；原有玩家窗口和旧包均未操作。打开原来的程序不会自动获得此次修改，需要启动上面的新版程序。

## 后续正式 Windows 构建

按用户追加要求，通过 `BuildConfig.BuildWindows` 清缓存重新构建，独立交付为 `Builds/Windows-ResultSummary-20260906-134650`，另附同名 ZIP（51,473,062 字节）。正式入口 `Builds/Windows/EchoRun.exe` 也已更新。

构建成功；发布目录的所有 15 个运行文件与原始构建逐一校验哈希一致。后台启动检查确认玩家资源、Direct3D 11 与输入系统正常初始化，12 秒后进程仍运行，无观察到的启动异常。验证结束已关闭本次测试进程，未发送游戏输入。这里补充的是启动验证，尚无新的整局人工试玩结果。

结算页源码与之前通过 658/45 项测试的哈希一致，打包步骤未重复运行测试。构建日志、启动日志、工作区前后状态与压缩包哈希位于 `TestResults/ResultSummaryRelease-20260906-134650`。构建未改变已备份的项目设置与道路贴图导入设置。

[启动正式新版](../Builds/Windows-ResultSummary-20260906-134650/EchoRun.exe) · [完整 ZIP](../Builds/Windows-ResultSummary-20260906-134650.zip) · [本次构建清单](../Builds/Windows-ResultSummary-20260906-134650/build-info.json)
