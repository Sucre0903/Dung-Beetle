using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Drawing;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

using Rhino;
using Rhino.Geometry;

namespace Sliding_Simulation
{
public class SmartPlanarUnionComponent : GH_Component
{
  // Dung Beetle - Smart Planar Union (Solid Boolean Method)
  // Adapted from the user's Grasshopper C# Script V3.3.
  // The original union/search/post-processing algorithm is preserved.
  //
  // IMPORTANT: Keep ComponentGuid stable after release.
  public SmartPlanarUnionComponent()
    : base(
        "Smart Planar Union",
        "PlanarUnion",
        "Intelligently unions overlapping coplanar surfaces using staged solid "
        + "Boolean operations, then extracts and simplifies planar regions. "
        + "V3.3 supports horizontal, single-face planar Breps at the same Z elevation.",
        "Dung Beetle",
        "Geometry")
  {
  }

  public override Guid ComponentGuid =>
    new Guid("17e90e37-ffa5-4bd6-bc4c-9cfdcfc8145e");

  protected override Bitmap Icon =>
    Dung_Beetle.Properties.Resources.SmartPlanarUnionIcon;

  protected override void RegisterInputParams(
    GH_Component.GH_InputParamManager pManager)
  {
    pManager.AddBrepParameter(
      "Surfaces", "S",
      "Horizontal single-face planar Breps at one elevation. "
      + "Non-planar, multi-face or non-coplanar inputs are rejected.",
      GH_ParamAccess.list);

    pManager.AddNumberParameter(
      "Tolerance", "Tol",
      "Rhino model-unit tolerance for Boolean union and Top join/merge. "
      + "Nonpositive values fall back to 0.01.",
      GH_ParamAccess.item, 0.01);

    pManager.AddBooleanParameter(
      "Run", "Run",
      "Start union computation. For large models, the synchronous "
      + "Boolean calculation can take a long time.",
      GH_ParamAccess.item, false);

    // Let the component emit its own explicit empty-input report.
    pManager[0].Optional = true;
  }

  // Four outputs instead of seven: Top / Groups / Failed / Log.
  // The original V3.3 solid Boolean method remains internal.
  protected override void RegisterOutputParams(
    GH_Component.GH_OutputParamManager pManager)
  {
    pManager.AddBrepParameter(
      "Top", "Top",
      "三维实体合并后还原到输入标高的顶面，已尝试 Join 和 Merge。",
      GH_ParamAccess.list);

    pManager.AddIntegerParameter(
      "Groups", "Groups",
      "数据树：每个分支代表一个最终实体合并组，包含原输入列表中"
      + "从 0 开始的成员索引。Top 经过整体 Join/Merge，"
      + "不保证与 Groups 分支逐项对应。",
      GH_ParamAccess.tree);

    pManager.AddBrepParameter(
      "Failed", "Failed",
      "输入预处理阶段被拒绝的原始 Brep。空输入无法作为 Brep 输出。",
      GH_ParamAccess.list);

    pManager.AddTextParameter(
      "Log", "Log",
      "简明中文运行日志：完成情况、合并统计、候选复核、关键异常。",
      GH_ParamAccess.item);
  }

  protected override void SolveInstance(IGH_DataAccess DA)
  {
    var surfaces = new List<Brep>();
    double tolerance = 0.01;
    bool run = false;

    DA.GetDataList(0, surfaces);
    DA.GetData(1, ref tolerance);
    DA.GetData(2, ref run);

    _lastSummary = null;

    object top = null;
    object solids = null;
    object groups = null;
    object failed = null;
    object issues = null;
    object report = null;
    object debug = null;

    // Keep all internal V3.3 outputs for calculations and diagnosis,
    // but expose only the four requested component parameters.
    ComputeCore(
      surfaces, tolerance, run,
      ref top, ref solids, ref groups, ref failed,
      ref issues, ref report, ref debug);

    var topList = top as List<Brep> ?? new List<Brep>();
    var groupList = groups as List<string> ?? new List<string>();
    var failedList = failed as List<Brep> ?? new List<Brep>();
    var issueList = issues as List<string> ?? new List<string>();
    var debugList = debug as List<string> ?? new List<string>();
    string reportText = report as string ?? string.Empty;

    // One branch per surviving Boolean-Union node.
    // The source IDs are zero-based indices of the original S input list.
    var groupTree = new GH_Structure<GH_Integer>();
    for (int i = 0; i < groupList.Count; i++)
    {
      GH_Path path = new GH_Path(i);
      groupTree.EnsurePath(path);

      foreach (string token in groupList[i].Split(','))
      {
        int sourceId;
        if (int.TryParse(
            token.Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out sourceId))
        {
          groupTree.Append(new GH_Integer(sourceId), path);
        }
      }
    }

    DA.SetDataList(0, topList);
    DA.SetDataTree(1, groupTree);
    DA.SetDataList(2, failedList);
    DA.SetData(3, BuildChineseLog(
      run, reportText, issueList, debugList));

    if (!run)
    {
      Message = "待运行";
    }
    else if (_lastSummary == null || _lastSummary.TopCount == 0)
    {
      Message = "错误";
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Error,
        _lastSummary == null
          ? ExplainInputError(reportText)
          : "没有得到可用顶面，请查看 Log 和 Failed。");
    }
    else if (_lastSummary.HasImportantWarnings)
    {
      Message = "需检查";
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Warning,
        "部分输入、运算或顶面处理存在需要核对的问题，请查看中文 Log。");
    }
    else
    {
      // Fully attempted bounding-box-only candidates are informational.
      // They do NOT by themselves justify a yellow GH warning.
      Message = "完成";
    }
  }

  private sealed class RunSummary
  {
    public int InputCount;
    public int ValidCount;
    public int RejectedInputs;
    public int GroupCount;
    public int TopCount;
    public int TopFaces;
    public int PairMerges;
    public int BatchMerges;
    public int BooleanCalls;
    public int BooleanExceptions;
    public int RejectedBooleanResults;
    public double Seconds;
    public int MergedTopBreps;
    public int RemovedTopFaces;
    public bool JoinAttempted;
    public bool JoinAccepted;
    public bool JoinRollback;
    public int MergeRollbacks;
    public int FallbackNodes;
    public int MissingTopNodes;
    public int RemainingCandidates;
    public int FullyAttemptedPairs;
    public int IncompletelyAttemptedPairs;
    public bool BudgetReached;
    public bool SearchException;
    public bool AuditException;

    public bool HasImportantWarnings
    {
      get
      {
        return RejectedInputs > 0 ||
          BooleanExceptions > 0 ||
          RejectedBooleanResults > 0 ||
          JoinRollback ||
          MergeRollbacks > 0 ||
          FallbackNodes > 0 ||
          MissingTopNodes > 0 ||
          IncompletelyAttemptedPairs > 0 ||
          BudgetReached ||
          SearchException ||
          AuditException;
      }
    }
  }

  private RunSummary _lastSummary;
  private int _booleanExceptions;

  private static string ExplainInputError(string internalReport)
  {
    if (internalReport.Contains("Input S is empty"))
      return "输入为空：请在 S 端连接至少一个水平平面 Brep。";
    if (internalReport.Contains("Tol must be less"))
      return "容差过大：Tol 必须小于内部拉伸高度（100 个模型单位）。";
    if (internalReport.Contains("No valid extruded solids"))
      return "没有可用实体：请检查输入是否为同一 Z 标高的水平单面 Brep。";
    return "计算没有正常完成，请检查输入几何和容差。";
  }

  private string BuildChineseLog(
    bool run, string internalReport,
    List<string> issues, List<string> debug)
  {
    if (!run)
      return "Smart Planar Union V3.3\n状态：待运行，请将 Run 设为 True。";

    RunSummary s = _lastSummary;
    if (s == null)
      return "Smart Planar Union V3.3\n状态：无法计算\n"
        + ExplainInputError(internalReport);

    var lines = new List<string>();
    string status = s.TopCount == 0
      ? "未得到有效顶面"
      : (s.HasImportantWarnings ? "完成，但有事项需检查" : "计算完成");

    lines.Add("Smart Planar Union V3.3｜" + status);
    lines.Add("输入：" + s.InputCount + " 个，有效 "
      + s.ValidCount + "，无效 " + s.RejectedInputs);
    lines.Add("结果：" + s.GroupCount + " 个合并组，Top "
      + s.TopCount + " 个（共 " + s.TopFaces + " 个面）");
    lines.Add("布尔合并：两两 " + s.PairMerges + " 次，批量 "
      + s.BatchMerges + " 次；总调用 " + s.BooleanCalls + " 次");
    lines.Add("顶面整理：Join "
      + (!s.JoinAttempted ? "无需执行"
         : s.JoinAccepted ? "成功"
         : "未通过校验")
      + "；Merge 减少 " + s.RemovedTopFaces
      + " 个面（涉及 " + s.MergedTopBreps + " 组）");
    lines.Add("耗时：" + s.Seconds.ToString(
      "F2", CultureInfo.InvariantCulture) + " 秒");

    if (s.RemainingCandidates > 0)
    {
      lines.Add("空间候选：" + s.RemainingCandidates
        + " 对包围盒相交；完整尝试 " + s.FullyAttemptedPairs
        + " 对，尚未完整尝试 " + s.IncompletelyAttemptedPairs
        + " 对。包围盒相交不等于实体重叠。");
    }

    var warnings = new List<string>();
    if (s.RejectedInputs > 0)
      warnings.Add(s.RejectedInputs + " 个输入未被接受（检查 Failed）");
    if (s.FallbackNodes > 0)
      warnings.Add(s.FallbackNodes + " 组顶面采用原始面回退，可能未真正合并");
    if (s.MissingTopNodes > 0)
      warnings.Add(s.MissingTopNodes + " 组未取得顶面");
    if (s.JoinRollback)
      warnings.Add("顶面 Join 结果未通过校验，已回退");
    if (s.MergeRollbacks > 0)
      warnings.Add(s.MergeRollbacks + " 次顶面 Merge 被回退");
    if (s.BudgetReached)
      warnings.Add("达到搜索时间或调用次数预算，可能尚未完成");
    if (s.IncompletelyAttemptedPairs > 0)
      warnings.Add("部分相交候选尚未完成所有容差尝试");
    if (s.BooleanExceptions > 0)
      warnings.Add(s.BooleanExceptions + " 次布尔运算抛出异常");
    if (s.RejectedBooleanResults > 0)
      warnings.Add(s.RejectedBooleanResults + " 个布尔结果未通过几何校验");
    if (s.SearchException)
      warnings.Add("合并搜索发生异常");
    if (s.AuditException)
      warnings.Add("剩余候选审计发生异常");

    foreach (string warning in warnings)
      lines.Add("注意：" + warning);

    // Do not dump per-pair issue/debug entries into a very long Panel.
    // Retain short exception details only when the run needs attention.
    if (warnings.Count > 0)
    {
      var details = new List<string>();
      foreach (string entry in issues.Concat(debug))
      {
        if (string.IsNullOrWhiteSpace(entry) ||
            entry.Contains("Bounding boxes interact;") ||
            entry.Contains("PAIR SUCCESS:") ||
            entry.Contains("BATCH SUCCESS:"))
          continue;

        if (entry.IndexOf("EXCEPTION", StringComparison.OrdinalIgnoreCase) >= 0 ||
            entry.IndexOf("exception:", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          string shortEntry = entry.Length > 160
            ? entry.Substring(0, 160) + "…" : entry;
          if (!details.Contains(shortEntry))
            details.Add(shortEntry);
        }
        if (details.Count >= 2)
          break;
      }
      foreach (string detail in details)
        lines.Add("异常详情：" + detail);
    }

    lines.Add("分组说明：Groups 每个分支保存原输入 S 的索引（从 0 开始）；"
      + "Top 整体 Join/Merge 后的顺序可能不同。");
    lines.Add("校核建议：复杂轮廓及孔洞仍应结合模型检查。");
    return string.Join("\n", lines);
  }


  // ============================================================
  // SMART PLANAR UNION V3.3 - LONG BUDGET / JOIN + MERGE FACES
  //
  // INPUTS
  //   S     List<Brep>  / List access
  //   Tol   double      / Item access
  //   Run   bool        / Item access
  //
  // INTERNAL CORE OUTPUTS (public GH outputs: Top, Groups, Failed, Log)
  //   Top, Solids, Groups, Failed, Issues, Report, Debug
  //
  // Ported from the single-file Rhino 8 GH C# Script.
  // ============================================================

  // Internal extrusion is measured in Rhino model units (100 mm
  // when the Rhino document uses millimeters). The exterior H
  // input is deliberately removed; the 3D Boolean method remains.
  private const double INTERNAL_EXTRUSION_HEIGHT = 100.0;

  // Search budgets are checked BETWEEN operations. A single
  // running RhinoCommon Boolean cannot be forcibly interrupted.
  private const double MAX_SECONDS = 180.0;
  private const int MAX_BOOLEAN_CALLS = 1200;

  // Keep V3.1's Fast -> Retry -> Batch strategy; only expand
  // the last-resort batch budget for difficult models.
  private const int MAX_BATCH_CALLS = 48;
  private const int MAX_BATCH_NEIGHBORS = 5;

  private sealed class Node
  {
    public int StateId;
    public Brep Solid;
    public BoundingBox Box;
    public double Volume;
    public int FaceCount;
    public List<int> SourceIds = new List<int>();
  }

  private sealed class Pair
  {
    public int A;
    public int B;
    public double Score;
  }

  private enum SearchPhase
  {
    Fast,
    Retry,
    Batch
  }

  private int _nextStateId;
  private int _booleanCalls;
  private int _successfulPairs;
  private int _successfulBatches;
  private int _cacheHits;
  private int _rejectedResults;

  private int _fastCalls;
  private int _retryCalls;
  private int _batchCalls;

  private double _fastBooleanSeconds;
  private double _retryBooleanSeconds;
  private double _batchBooleanSeconds;

  private double _tol;
  private Stopwatch _watch;
  private List<string> _log;
  private HashSet<string> _attempted;


  // ============================================================
  // V3.3 TOP-ONLY POSTPROCESS STATISTICS
  // ============================================================

  private sealed class TopPostStats
  {
    public int RawBreps;
    public int RawFaces;
    public int JoinedBreps;
    public int JoinedFaces;
    public int FinalBreps;
    public int FinalFaces;
    public int MergedBreps;
    public int MergedFacesRemoved;
    public int MergeRollbacks;
    public bool JoinAttempted;
    public bool JoinAccepted;
    public bool JoinRollback;
    public double RawArea;
    public double FinalArea;
    public double JoinSeconds;
    public double MergeSeconds;
  }

  // ============================================================
  // MAIN
  // ============================================================

  private void ComputeCore(
		List<Brep> S,
		double Tol,
		bool Run,
		ref object Top,
		ref object Solids,
		ref object Groups,
		ref object Failed,
		ref object Issues,
		ref object Report,
		ref object Debug)
  {
    var topOutput = new List<Brep>();
    var solidOutput = new List<Brep>();
    var groupOutput = new List<string>();
    var failedOutput = new List<Brep>();
    var issueOutput = new List<string>();
    var debugOutput = new List<string>();

    Top = topOutput;
    Solids = solidOutput;
    Groups = groupOutput;
    Failed = failedOutput;
    Issues = issueOutput;
    Report = "Ready. Set Run to true.";
    Debug = debugOutput;

    if (!Run)
      return;

    _tol = Tol > 0.0 ? Tol : 0.01;
    _watch = Stopwatch.StartNew();
    _log = debugOutput;
    _attempted = new HashSet<string>();

    _nextStateId = 1;
    _booleanCalls = 0;
    _successfulPairs = 0;
    _successfulBatches = 0;
    _cacheHits = 0;
    _rejectedResults = 0;
    _booleanExceptions = 0;

    _fastCalls = 0;
    _retryCalls = 0;
    _batchCalls = 0;

    _fastBooleanSeconds = 0.0;
    _retryBooleanSeconds = 0.0;
    _batchBooleanSeconds = 0.0;

    if (S == null || S.Count == 0)
    {
      Report = "ERROR: Input S is empty.";
      return;
    }

    if (INTERNAL_EXTRUSION_HEIGHT <= _tol)
    {
      Report = "ERROR: Tol must be less than internal extrusion height (100 model units).";
      return;
    }

    // ==========================================================
    // 1. PREPARE REAL 3D SOLIDS
    // ==========================================================

    var nodes = new List<Node>();
    // Preserve normalized source faces for emergency output only.
    // These are not a substitute for a successful Boolean Union.
    var sourceTopFallback = new Dictionary<int, Brep>();
    double baseZ = double.NaN;
    int rejectedInputs = 0;

    for (int i = 0; i < S.Count; i++)
    {
      Brep source = S[i];
      try
      {

      if (source == null)
      {
        rejectedInputs++;
        _log.Add("Input " + i + ": null.");
        continue;
      }

      if (!source.IsValid || source.Faces.Count != 1)
      {
        failedOutput.Add(source);
        rejectedInputs++;
        _log.Add(
          "Input " + i +
          ": invalid Brep or not a single-face Brep."
        );
        continue;
      }

      Plane plane;

      if (!source.Faces[0].TryGetPlane(out plane, _tol))
      {
        failedOutput.Add(source);
        rejectedInputs++;
        _log.Add("Input " + i + ": not planar.");
        continue;
      }

      if (Math.Abs(plane.Normal.Z) < 0.999999)
      {
        failedOutput.Add(source);
        rejectedInputs++;
        _log.Add(
          "Input " + i + ": not parallel to XY."
        );
        continue;
      }

      BoundingBox sourceBox = source.GetBoundingBox(true);

      if (!sourceBox.IsValid ||
          sourceBox.Max.Z - sourceBox.Min.Z > _tol)
      {
        failedOutput.Add(source);
        rejectedInputs++;
        _log.Add(
          "Input " + i + ": invalid Z range."
        );
        continue;
      }

      double sourceZ =
        0.5 * (sourceBox.Min.Z + sourceBox.Max.Z);

      if (double.IsNaN(baseZ))
        baseZ = sourceZ;

      if (Math.Abs(sourceZ - baseZ) > _tol)
      {
        failedOutput.Add(source);
        rejectedInputs++;
        _log.Add(
          "Input " + i +
          ": Z differs from reference by more than Tol."
        );
        continue;
      }

      Brep prepared = source.DuplicateBrep();

      if (prepared == null)
      {
        failedOutput.Add(source);
        rejectedInputs++;
        _log.Add(
          "Input " + i + ": duplication failed."
        );
        continue;
      }

      prepared.Transform(
        Transform.Translation(
          0.0,
          0.0,
          baseZ - sourceZ
        )
      );

      Brep extrusion = null;

      try
      {
        Curve path = new LineCurve(
          new Point3d(0.0, 0.0, 0.0),
          new Point3d(0.0, 0.0, INTERNAL_EXTRUSION_HEIGHT)
        );

        extrusion = prepared.Faces[0].CreateExtrusion(
          path,
          true
        );
      }
      catch (Exception ex)
      {
        _log.Add(
          "Input " + i +
          ": extrusion exception: " + ex.Message
        );
      }

      if (!IsUsableSolid(extrusion))
      {
        failedOutput.Add(source);
        rejectedInputs++;
        _log.Add(
          "Input " + i +
          ": extrusion is not a valid closed solid."
        );
        continue;
      }

      OrientOutward(extrusion);

      Node node = CreateNode(
        extrusion,
        new List<int> { i }
      );

      if (node == null)
      {
        failedOutput.Add(source);
        rejectedInputs++;
        _log.Add(
          "Input " + i +
          ": invalid extrusion volume or bounding box."
        );
        continue;
      }

      nodes.Add(node);
      sourceTopFallback[i] = prepared;
      }
      catch (Exception ex)
      {
        if (source != null)
          failedOutput.Add(source);
        rejectedInputs++;
        _log.Add("Input " + i + ": unexpected preparation exception: " + ex.Message);
      }
    }

    int validInputs = nodes.Count;

    if (nodes.Count == 0)
    {
      Report = "ERROR: No valid extruded solids.";
      return;
    }

    // ==========================================================
    // 2. FAST GROWTH
    //
    // Each current-state pair is first attempted ONLY at Tol.
    // A successful union creates a new node and triggers a
    // new candidate search.
    // ==========================================================

    int fastMerges = 0;
    int retryMerges = 0;
    int postRetryFastMerges = 0;
    int batchMerges = 0;
    int postBatchFastMerges = 0;
    bool searchException = false;

    // An unexpected exception must not prevent the surviving
    // validated solids from reaching the output stage.
    try
    {
      fastMerges = GrowPairs(
        nodes,
        SearchPhase.Fast,
        new double[] { _tol }
      );

      // ==========================================================
      // 3. TARGETED RETRIES AND DEFERRED BATCH SEARCH
      // ==========================================================
      bool changed = true;

      while (changed && HasBudget())
      {
        changed = false;

        int retryNow = GrowPairs(
          nodes,
          SearchPhase.Retry,
          new double[] { _tol * 0.5, _tol * 2.0 }
        );

        retryMerges += retryNow;

        if (retryNow > 0)
        {
          changed = true;

          int fastNow = GrowPairs(
            nodes,
            SearchPhase.Fast,
            new double[] { _tol }
          );

          postRetryFastMerges += fastNow;
          continue;
        }

        if (nodes.Count >= 3 &&
            _batchCalls < MAX_BATCH_CALLS &&
            HasBudget())
        {
          bool batchChanged = TryOneBatchGrowth(nodes);

          if (batchChanged)
          {
            changed = true;
            batchMerges++;

            int fastNow = GrowPairs(
              nodes,
              SearchPhase.Fast,
              new double[] { _tol }
            );

            postBatchFastMerges += fastNow;
          }
        }
      }
    }
    catch (Exception ex)
    {
      searchException = true;
      string message = "SEARCH EXCEPTION: " + ex.Message +
        " | Best-effort output uses surviving solid nodes.";
      issueOutput.Add(message);
      _log.Add(message);
    }

    // ==========================================================
    // 4. REMAINING CANDIDATE AUDIT
    //
    // This is a bounding-box audit, NOT a proof of real
    // volume overlap. Unattempted current-state pairs are
    // explicitly distinguished from attempted ones.
    // ==========================================================

    List<Pair> remaining = new List<Pair>();
    int untestedCurrentPairs = 0;
    int attemptedCurrentPairs = 0;
    bool auditException = false;

    try
    {
      remaining = BuildPairCandidates(nodes);

      foreach (Pair pair in remaining)
      {
        Node a = nodes[pair.A];
        Node b = nodes[pair.B];

        bool fastAttempted = WasAttempted(
          new List<Node> { a, b }, _tol
        );
        bool retrySmallAttempted = WasAttempted(
          new List<Node> { a, b }, _tol * 0.5
        );
        bool retryLargeAttempted = WasAttempted(
          new List<Node> { a, b }, _tol * 2.0
        );

        if (fastAttempted &&
            retrySmallAttempted &&
            retryLargeAttempted)
          attemptedCurrentPairs++;
        else
          untestedCurrentPairs++;

        issueOutput.Add(
          "[" + SourceText(a) + "] vs [" +
          SourceText(b) + "] | " +
          "Tol attempted: " + fastAttempted +
          " | Tol*0.5 attempted: " + retrySmallAttempted +
          " | Tol*2 attempted: " + retryLargeAttempted +
          " | Bounding boxes interact; " +
          "actual volume overlap is unverified."
        );
      }
    }
    catch (Exception ex)
    {
      auditException = true;
      string message = "AUDIT EXCEPTION: " + ex.Message;
      issueOutput.Add(message);
      _log.Add(message);
    }

    // ==========================================================
    // 5. OUTPUT SOLIDS AND RESTORE TOP TO INPUT Z
    // ==========================================================

    double targetTopZ = baseZ + INTERNAL_EXTRUSION_HEIGHT;
    int topFallbackNodes = 0;
    int topFallbackFaces = 0;
    int topMissingNodes = 0;

    for (int i = 0; i < nodes.Count; i++)
    {
      Node node = nodes[i];

      solidOutput.Add(node.Solid);
      groupOutput.Add(SourceText(node));

      int startTopCount = topOutput.Count;
      bool extractionException = false;

      try
      {
        foreach (BrepFace face in node.Solid.Faces)
        {
          Plane facePlane;

          if (!face.TryGetPlane(out facePlane, _tol))
            continue;

          if (Math.Abs(facePlane.Normal.Z) < 0.999999)
            continue;

          BoundingBox faceBox = face.GetBoundingBox(true);

          if (!faceBox.IsValid)
            continue;

          if (Math.Abs(faceBox.Min.Z - targetTopZ) > _tol ||
              Math.Abs(faceBox.Max.Z - targetTopZ) > _tol)
            continue;

          Brep top = face.DuplicateFace(false);

          if (top == null || !top.IsValid)
            continue;

          // Only the independent Top copy moves down.
          // The 3D solids stay at their original elevations.
          if (top.Transform(Transform.Translation(
                0.0, 0.0, -INTERNAL_EXTRUSION_HEIGHT)) &&
              top.IsValid)
            topOutput.Add(top);
        }
      }
      catch (Exception ex)
      {
        extractionException = true;
        _log.Add("TOP EXTRACTION EXCEPTION [" +
          SourceText(node) + "]: " + ex.Message);
      }

      // If a node yielded no reliable top, return normalized
      // source faces as a clearly labeled LAST-RESORT fallback.
      // This does not imply that the union was successful: input
      // faces may overlap and their geometric union is unverified.
      if (extractionException || topOutput.Count == startTopCount)
      {
        if (extractionException &&
            topOutput.Count > startTopCount)
          topOutput.RemoveRange(
            startTopCount,
            topOutput.Count - startTopCount
          );

        int added = 0;

        foreach (int id in node.SourceIds)
        {
          Brep preparedTop;
          if (!sourceTopFallback.TryGetValue(
                id, out preparedTop) ||
              preparedTop == null)
            continue;

          try
          {
            Brep rawTop = preparedTop.DuplicateBrep();
            if (rawTop != null && rawTop.IsValid)
            {
              topOutput.Add(rawTop);
              added++;
            }
          }
          catch (Exception ex)
          {
            _log.Add("TOP FALLBACK COPY EXCEPTION input " +
              id + ": " + ex.Message);
          }
        }

        topFallbackNodes++;
        topFallbackFaces += added;
        if (added == 0)
          topMissingNodes++;

        string reason = extractionException
          ? "extraction exception" : "no matching top face";

        issueOutput.Add(
          "TOP FALLBACK [" + SourceText(node) +
          "]: " + reason + " | original normalized faces=" +
          added + " | NOT a verified union; overlap possible."
        );
      }
    }


    // ==========================================================
    // 6. V3.3 TOP POSTPROCESS: JOIN -> MERGE COPLANAR FACES
    //
    // V3.2 solid union, Top extraction, and source-face fallback
    // have completed above and are not changed by this stage.
    // Only independent Top Breps are processed here.
    // ==========================================================

    TopPostStats topPost = new TopPostStats();
    List<Brep> originalTopOutput = topOutput;

    // Snapshot the existing search-budget result before postprocess
    // work. Join and Merge do not consume Boolean-search calls.
    bool searchBudgetReached =
      _booleanCalls >= MAX_BOOLEAN_CALLS ||
      _watch.Elapsed.TotalSeconds >= MAX_SECONDS;

    try
    {
      topOutput = JoinAndMergeTop(
        originalTopOutput, baseZ, topPost, issueOutput
      );
    }
    catch (Exception ex)
    {
      // Last-level guard: preserve the exact V3.2 Top list.
      topOutput = originalTopOutput;
      topPost.JoinRollback = true;
      topPost.FinalBreps = topOutput.Count;
      topPost.FinalFaces = CountTopFaces(topOutput);

      string message =
        "TOP POSTPROCESS EXCEPTION: " + ex.Message +
        " | All original V3.2 Top Breps retained.";
      issueOutput.Add(message);
      _log.Add(message);
    }

    // ==========================================================
    // 7. REPORT
    // ==========================================================

    _watch.Stop();

    bool budgetReached = searchBudgetReached;

    bool reviewRequired =
      budgetReached ||
      searchException ||
      auditException ||
      topFallbackNodes > 0 ||
      rejectedInputs > 0 ||
      remaining.Count > 0 ||
      topPost.JoinRollback ||
      topPost.MergeRollbacks > 0;

    string status = topOutput.Count == 0
      ? "NO VALID TOP AVAILABLE; INSPECT FAILED AND DEBUG"
      : (reviewRequired
          ? "BEST-EFFORT TOP AVAILABLE; REVIEW ISSUES"
          : "TOP AVAILABLE; CONFIGURED SEARCH FINISHED");

    Report =
      "SMART PLANAR UNION V3.3 - JOIN + MERGE FACES\n" +
      "================================\n" +
      "Input SRFs: " + S.Count + "\n" +
      "Valid extrusions: " + validInputs + "\n" +
      "Rejected inputs: " + rejectedInputs + "\n" +
      "Output solids: " + solidOutput.Count + "\n" +
      "Raw extracted Top Breps: " + topPost.RawBreps + "\n" +
      "Raw extracted Top faces: " + topPost.RawFaces + "\n" +
      "Output Top Breps (after Join/Merge): " +
        topOutput.Count + "\n" +
      "Output Top faces (after Join/Merge): " +
        CountTopFaces(topOutput) + "\n" +
      "Internal extrusion: " +
        INTERNAL_EXTRUSION_HEIGHT.ToString("F3") +
        " model units\n" +
      "Top fallback nodes: " + topFallbackNodes + "\n" +
      "Top fallback source faces: " + topFallbackFaces + "\n" +
      "Top nodes still missing: " + topMissingNodes + "\n" +
      "--------------------------------\n" +
      "Top Join attempted: " + topPost.JoinAttempted + "\n" +
      "Top Join accepted: " + topPost.JoinAccepted + "\n" +
      "Top Join rollback: " + topPost.JoinRollback + "\n" +
      "Top Breps after Join: " + topPost.JoinedBreps + "\n" +
      "Top faces after Join: " + topPost.JoinedFaces + "\n" +
      "Top Breps successfully face-merged: " +
        topPost.MergedBreps + "\n" +
      "Top faces removed by Merge: " +
        topPost.MergedFacesRemoved + "\n" +
      "Top Merge rollbacks: " + topPost.MergeRollbacks + "\n" +
      "Top area before/after: " +
        topPost.RawArea.ToString("G17") + " / " +
        topPost.FinalArea.ToString("G17") + "\n" +
      "Top Join time: " +
        topPost.JoinSeconds.ToString("F3") + " s\n" +
      "Top Merge time: " +
        topPost.MergeSeconds.ToString("F3") + " s\n" +
      "Successful pair unions: " +
        _successfulPairs + "\n" +
      "Successful batch unions: " +
        _successfulBatches + "\n" +
      "Initial fast merges: " + fastMerges + "\n" +
      "Retry merges: " + retryMerges + "\n" +
      "Fast merges after retries: " +
        postRetryFastMerges + "\n" +
      "Batch merges: " + batchMerges + "\n" +
      "Fast merges after batches: " +
        postBatchFastMerges + "\n" +
      "--------------------------------\n" +
      "Boolean calls TOTAL: " +
        _booleanCalls + "\n" +
      "Fast Boolean calls: " + _fastCalls + "\n" +
      "Retry Boolean calls: " + _retryCalls + "\n" +
      "Batch Boolean calls: " + _batchCalls + "\n" +
      "Cached attempts skipped: " +
        _cacheHits + "\n" +
      "Rejected Boolean results: " +
        _rejectedResults + "\n" +
      "--------------------------------\n" +
      "Fast Boolean time: " +
        _fastBooleanSeconds.ToString("F3") + " s\n" +
      "Retry Boolean time: " +
        _retryBooleanSeconds.ToString("F3") + " s\n" +
      "Batch Boolean time: " +
        _batchBooleanSeconds.ToString("F3") + " s\n" +
      "Total elapsed: " +
        _watch.Elapsed.TotalSeconds.ToString("F3") + " s\n" +
      "Search time budget: " + MAX_SECONDS +
        " s (not a hard Boolean timeout)\n" +
      "Search call budget: " + MAX_BOOLEAN_CALLS + "\n" +
      "Budget reached: " + budgetReached + "\n" +
      "Unexpected search exception: " +
        searchException + "\n" +
      "Audit exception: " + auditException + "\n" +
      "--------------------------------\n" +
      "Remaining box candidates: " +
        remaining.Count + "\n" +
      "Current-state fully attempted pairs: " +
        attemptedCurrentPairs + "\n" +
      "Current-state incompletely attempted pairs: " +
        untestedCurrentPairs + "\n" +
      "Input reference Z: " +
        baseZ.ToString("F6") + " model units\n" +
      "Top returned to reference Z: YES\n" +
      "Status: " + status + "\n" +
      "NOTE: Budget stop preserves surviving valid solids. " +
      "Top fallback faces are NOT a completed union. " +
      "Bounding-box candidates are not confirmed overlaps. " +
      "Geometry and holes still require review. " +
      "Join/Merge validates area, bounds, planarity, and " +
      "manifold edges; this does NOT prove exact region coverage.";

    _lastSummary = new RunSummary
    {
      InputCount = S.Count,
      ValidCount = validInputs,
      RejectedInputs = rejectedInputs,
      GroupCount = nodes.Count,
      TopCount = topOutput.Count,
      TopFaces = CountTopFaces(topOutput),
      PairMerges = _successfulPairs,
      BatchMerges = _successfulBatches,
      BooleanCalls = _booleanCalls,
      BooleanExceptions = _booleanExceptions,
      RejectedBooleanResults = _rejectedResults,
      Seconds = _watch.Elapsed.TotalSeconds,
      MergedTopBreps = topPost.MergedBreps,
      RemovedTopFaces = topPost.MergedFacesRemoved,
      JoinAttempted = topPost.JoinAttempted,
      JoinAccepted = topPost.JoinAccepted,
      JoinRollback = topPost.JoinRollback,
      MergeRollbacks = topPost.MergeRollbacks,
      FallbackNodes = topFallbackNodes,
      MissingTopNodes = topMissingNodes,
      RemainingCandidates = remaining.Count,
      FullyAttemptedPairs = attemptedCurrentPairs,
      IncompletelyAttemptedPairs = untestedCurrentPairs,
      BudgetReached = budgetReached,
      SearchException = searchException,
      AuditException = auditException
    };

    Top = topOutput;
    Solids = solidOutput;
    Groups = groupOutput;
    Failed = failedOutput;
    Issues = issueOutput;
    Debug = debugOutput;
  }


  // ============================================================
  // V3.3 TOP POSTPROCESS HELPERS
  // ============================================================

  private int CountTopFaces(IEnumerable<Brep> breps)
  {
    if (breps == null) return 0;
    int count = 0;
    foreach (Brep brep in breps)
      if (brep != null)
        count += brep.Faces.Count;
    return count;
  }

  // A sum of surface areas is deliberately used here, rather than
  // claiming a measured planar union area. It still accounts for
  // any already-unresolved overlaps in the V3.2 Top output.
  private bool TryTopMetrics(
    IEnumerable<Brep> breps,
    double referenceZ,
    out double area,
    out BoundingBox bounds)
  {
    area = 0.0;
    bounds = BoundingBox.Empty;

    if (breps == null)
      return false;

    int count = 0;

    try
    {
      foreach (Brep brep in breps)
      {
        if (brep == null || !brep.IsValid ||
            brep.Faces.Count < 1)
          return false;

        foreach (BrepEdge edge in brep.Edges)
        {
          if (edge.Valence == EdgeAdjacency.NonManifold)
            return false;
        }

        foreach (BrepFace face in brep.Faces)
        {
          Plane plane;
          if (!face.TryGetPlane(out plane, _tol) ||
              Math.Abs(plane.Normal.Z) < 0.999999)
            return false;
        }

        BoundingBox box = brep.GetBoundingBox(true);
        if (!box.IsValid ||
            Math.Abs(box.Min.Z - referenceZ) > _tol * 2.0 ||
            Math.Abs(box.Max.Z - referenceZ) > _tol * 2.0)
          return false;

        double thisArea;
        using (AreaMassProperties properties =
          AreaMassProperties.Compute(
            brep, true, false, false, false))
        {
          if (properties == null)
            return false;
          thisArea = Math.Abs(properties.Area);
        }

        if (double.IsNaN(thisArea) ||
            double.IsInfinity(thisArea) ||
            thisArea <= 0.0)
          return false;

        area += thisArea;
        bounds = BoundingBox.Union(bounds, box);
        count++;
      }
    }
    catch
    {
      return false;
    }

    return count > 0 &&
      bounds.IsValid &&
      !double.IsNaN(area) &&
      !double.IsInfinity(area) &&
      area > 0.0;
  }

  private bool SameTopBounds(BoundingBox a, BoundingBox b)
  {
    if (!a.IsValid || !b.IsValid)
      return false;

    double distanceTol = Math.Max(1e-8, _tol * 2.0);
    return
      Math.Abs(a.Min.X - b.Min.X) <= distanceTol &&
      Math.Abs(a.Min.Y - b.Min.Y) <= distanceTol &&
      Math.Abs(a.Min.Z - b.Min.Z) <= distanceTol &&
      Math.Abs(a.Max.X - b.Max.X) <= distanceTol &&
      Math.Abs(a.Max.Y - b.Max.Y) <= distanceTol &&
      Math.Abs(a.Max.Z - b.Max.Z) <= distanceTol;
  }

  private bool SameTopArea(double before, double after)
  {
    // A small numeric allowance, not a proof of equal regions.
    double areaTol = Math.Max(
      Math.Max(1e-8, _tol * _tol * 0.1),
      Math.Abs(before) * 1e-6
    );
    return
      !double.IsNaN(before) && !double.IsNaN(after) &&
      !double.IsInfinity(before) && !double.IsInfinity(after) &&
      Math.Abs(before - after) <= areaTol;
  }

  private bool TryNakedPerimeter(Brep brep, out double length)
  {
    length = 0.0;
    if (brep == null || !brep.IsValid)
      return false;

    try
    {
      foreach (BrepEdge edge in brep.Edges)
      {
        if (edge.Valence == EdgeAdjacency.NonManifold)
          return false;
        if (edge.Valence != EdgeAdjacency.Naked)
          continue;

        double edgeLength = edge.GetLength();
        if (double.IsNaN(edgeLength) ||
            double.IsInfinity(edgeLength) ||
            edgeLength < 0.0)
          return false;
        length += edgeLength;
      }
    }
    catch
    {
      return false;
    }

    return !double.IsNaN(length) &&
      !double.IsInfinity(length);
  }

  private List<Brep> JoinAndMergeTop(
    List<Brep> original,
    double referenceZ,
    TopPostStats stats,
    List<string> issues)
  {
    stats.RawBreps = original == null ? 0 : original.Count;
    stats.RawFaces = CountTopFaces(original);
    stats.JoinedBreps = stats.RawBreps;
    stats.JoinedFaces = stats.RawFaces;
    stats.FinalBreps = stats.RawBreps;
    stats.FinalFaces = stats.RawFaces;

    if (original == null || original.Count == 0)
      return original ?? new List<Brep>();

    double rawArea;
    BoundingBox rawBounds;
    if (!TryTopMetrics(
          original, referenceZ, out rawArea, out rawBounds))
    {
      stats.JoinRollback = true;
      string warning =
        "TOP POSTPROCESS SKIPPED: V3.2 raw Top failed " +
        "planarity, validity, or area validation. " +
        "Original Top retained.";
      issues.Add(warning);
      _log.Add(warning);
      return original;
    }

    stats.RawArea = rawArea;
    stats.FinalArea = rawArea;

    List<Brep> joined = original;
    if (original.Count > 1)
    {
      stats.JoinAttempted = true;
      Stopwatch joinWatch = Stopwatch.StartNew();

      try
      {
        // RhinoCommon may rework or sew topology. Pass copies so
        // a failure cannot mutate V3.2's output Breps.
        List<Brep> copies = new List<Brep>();
        foreach (Brep brep in original)
        {
          Brep copy = brep.DuplicateBrep();
          if (copy == null || !copy.IsValid)
            throw new InvalidOperationException(
              "Unable to duplicate an input Top Brep.");
          copies.Add(copy);
        }

        Brep[] joinedArray =
          Brep.JoinBreps(copies, _tol);

        double joinedArea;
        BoundingBox joinedBounds;

        if (joinedArray != null &&
            joinedArray.Length > 0 &&
            TryTopMetrics(
              joinedArray, referenceZ,
              out joinedArea, out joinedBounds) &&
            SameTopArea(rawArea, joinedArea) &&
            SameTopBounds(rawBounds, joinedBounds))
        {
          joined = joinedArray.ToList();
          stats.JoinAccepted = true;
        }
        else
        {
          stats.JoinRollback = true;
          string warning =
            "TOP JOIN ROLLBACK: empty/invalid result, " +
            "non-manifold edge, area mismatch, or changed bounds. " +
            "Original V3.2 Top retained.";
          issues.Add(warning);
          _log.Add(warning);
        }
      }
      catch (Exception ex)
      {
        stats.JoinRollback = true;
        string warning =
          "TOP JOIN EXCEPTION: " + ex.Message +
          " | Original V3.2 Top retained.";
        issues.Add(warning);
        _log.Add(warning);
      }
      finally
      {
        joinWatch.Stop();
        stats.JoinSeconds = joinWatch.Elapsed.TotalSeconds;
      }
    }

    stats.JoinedBreps = joined.Count;
    stats.JoinedFaces = CountTopFaces(joined);

    // A failed global Join is not retried with looser tolerance.
    // The fallback faces are still returned, unchanged.
    if (stats.JoinRollback)
      return original;

    List<Brep> result = new List<Brep>();
    Stopwatch mergeWatch = Stopwatch.StartNew();

    for (int i = 0; i < joined.Count; i++)
    {
      Brep before = joined[i];

      if (before == null || before.Faces.Count < 2)
      {
        result.Add(before);
        continue;
      }

      try
      {
        Brep trial = before.DuplicateBrep();
        if (trial == null)
          throw new InvalidOperationException(
            "Unable to duplicate joined Top Brep.");

        int originalFaces = before.Faces.Count;

        // All eligible coplanar faces inside this joined Brep.
        // A false return means "nothing merged", not failure.
        if (!trial.MergeCoplanarFaces(_tol))
        {
          result.Add(before);
          continue;
        }

        double beforeArea, afterArea;
        BoundingBox beforeBounds, afterBounds;
        double beforePerimeter, afterPerimeter;

        bool acceptable =
          TryTopMetrics(new Brep[] { before }, referenceZ,
            out beforeArea, out beforeBounds) &&
          TryTopMetrics(new Brep[] { trial }, referenceZ,
            out afterArea, out afterBounds) &&
          SameTopArea(beforeArea, afterArea) &&
          SameTopBounds(beforeBounds, afterBounds) &&
          TryNakedPerimeter(before, out beforePerimeter) &&
          TryNakedPerimeter(trial, out afterPerimeter) &&
          Math.Abs(beforePerimeter - afterPerimeter) <=
            Math.Max(
              Math.Max(1e-8, _tol * 0.5),
              beforePerimeter * 1e-6
            ) &&
          trial.Faces.Count <= originalFaces;

        if (acceptable)
        {
          result.Add(trial);
          stats.MergedBreps++;
          stats.MergedFacesRemoved +=
            originalFaces - trial.Faces.Count;
        }
        else
        {
          result.Add(before);
          stats.MergeRollbacks++;
          string warning =
            "TOP MERGE ROLLBACK at joined Brep " + i +
            ": invalid topology, changed area/bounds, " +
            "or changed naked perimeter.";
          issues.Add(warning);
          _log.Add(warning);
        }
      }
      catch (Exception ex)
      {
        result.Add(before);
        stats.MergeRollbacks++;
        string warning =
          "TOP MERGE EXCEPTION at joined Brep " + i +
          ": " + ex.Message + " | Pre-merge Brep retained.";
        issues.Add(warning);
        _log.Add(warning);
      }
    }

    mergeWatch.Stop();
    stats.MergeSeconds = mergeWatch.Elapsed.TotalSeconds;
    stats.FinalBreps = result.Count;
    stats.FinalFaces = CountTopFaces(result);

    double finalArea;
    BoundingBox finalBounds;
    if (!TryTopMetrics(
          result, referenceZ, out finalArea, out finalBounds) ||
        !SameTopArea(rawArea, finalArea) ||
        !SameTopBounds(rawBounds, finalBounds))
    {
      // Defensive final transaction rollback; no Solid changed.
      string warning =
        "TOP POSTPROCESS FINAL ROLLBACK: final set failed " +
        "overall area/bounds/validity check. " +
        "Original V3.2 Top retained.";
      issues.Add(warning);
      _log.Add(warning);
      stats.JoinRollback = true;
      stats.FinalBreps = original.Count;
      stats.FinalFaces = CountTopFaces(original);
      stats.FinalArea = rawArea;
      return original;
    }

    stats.FinalArea = finalArea;
    return result;
  }

  // ============================================================
  // BUDGET
  // ============================================================

  private bool HasBudget()
  {
    return
      _watch != null &&
      _booleanCalls < MAX_BOOLEAN_CALLS &&
      _watch.Elapsed.TotalSeconds < MAX_SECONDS;
  }

  // ============================================================
  // NODE / GEOMETRY HELPERS
  // ============================================================

  private Node CreateNode(
    Brep solid,
    List<int> sourceIds)
  {
    if (!IsUsableSolid(solid))
      return null;

    double volume = SolidVolume(solid);
    BoundingBox box = solid.GetBoundingBox(true);

    if (double.IsNaN(volume) ||
        volume <= 0.0 ||
        !box.IsValid)
      return null;

    Node node = new Node();

    node.StateId = _nextStateId++;
    node.Solid = solid;
    node.Box = box;
    node.Volume = volume;
    node.FaceCount = solid.Faces.Count;
    node.SourceIds = sourceIds
      .Distinct()
      .OrderBy(x => x)
      .ToList();

    return node;
  }

  private Node MakeMergedNode(
    List<Node> participants,
    Brep union)
  {
    List<int> sourceIds = participants
      .SelectMany(x => x.SourceIds)
      .Distinct()
      .OrderBy(x => x)
      .ToList();

    return CreateNode(union, sourceIds);
  }

  private bool IsUsableSolid(Brep brep)
  {
    return
      brep != null &&
      brep.IsValid &&
      brep.IsSolid;
  }

  private void OrientOutward(Brep brep)
  {
    if (brep == null)
      return;

    if (brep.SolidOrientation ==
        BrepSolidOrientation.Inward)
      brep.Flip();
  }

  private double SolidVolume(Brep brep)
  {
    if (!IsUsableSolid(brep))
      return double.NaN;

    try
    {
      using (
        VolumeMassProperties properties =
          VolumeMassProperties.Compute(brep)
      )
      {
        if (properties == null)
          return double.NaN;

        return Math.Abs(properties.Volume);
      }
    }
    catch
    {
      return double.NaN;
    }
  }

  private string SourceText(Node node)
  {
    return string.Join(
      ",",
      node.SourceIds.OrderBy(x => x)
    );
  }

  // ============================================================
  // BOX FILTER AND CANDIDATE SCHEDULING
  //
  // Node boxes are cached. They are recalculated only when
  // a new merged node is created.
  // ============================================================

  private bool BoxesMayInteract(
    BoundingBox a,
    BoundingBox b)
  {
    if (!a.IsValid || !b.IsValid)
      return false;

    return !(
      a.Max.X < b.Min.X - _tol ||
      b.Max.X < a.Min.X - _tol ||
      a.Max.Y < b.Min.Y - _tol ||
      b.Max.Y < a.Min.Y - _tol ||
      a.Max.Z < b.Min.Z - _tol ||
      b.Max.Z < a.Min.Z - _tol
    );
  }

  private double BoxOverlapScore(
    BoundingBox a,
    BoundingBox b)
  {
    double dx = Math.Max(
      0.0,
      Math.Min(a.Max.X, b.Max.X) -
      Math.Max(a.Min.X, b.Min.X)
    );

    double dy = Math.Max(
      0.0,
      Math.Min(a.Max.Y, b.Max.Y) -
      Math.Max(a.Min.Y, b.Min.Y)
    );

    double dz = Math.Max(
      0.0,
      Math.Min(a.Max.Z, b.Max.Z) -
      Math.Max(a.Min.Z, b.Min.Z)
    );

    return dx * dy * dz;
  }

  private List<Pair> BuildPairCandidates(
    List<Node> nodes)
  {
    var candidates = new List<Pair>();

    for (int i = 0; i < nodes.Count; i++)
    {
      for (int j = i + 1; j < nodes.Count; j++)
      {
        Node a = nodes[i];
        Node b = nodes[j];

        if (!BoxesMayInteract(a.Box, b.Box))
          continue;

        Pair pair = new Pair();

        pair.A = i;
        pair.B = j;

        pair.Score =
          BoxOverlapScore(a.Box, b.Box) /
          (1.0 + 0.01 * (a.FaceCount + b.FaceCount));

        candidates.Add(pair);
      }
    }

    return candidates
      .OrderByDescending(x => x.Score)
      .ThenBy(
        x =>
          nodes[x.A].FaceCount +
          nodes[x.B].FaceCount
      )
      .ToList();
  }

  // ============================================================
  // ATTEMPT CACHE
  //
  // Cache is keyed by current geometry state IDs and tolerance.
  // When a cluster grows, it receives a new StateId.
  // ============================================================

  private string OperationKey(
    List<Node> participants,
    double tolerance)
  {
    string ids = string.Join(
      "-",
      participants
        .Select(x => x.StateId)
        .OrderBy(x => x)
    );

    return
      ids + "|" +
      tolerance.ToString(
        "R",
        CultureInfo.InvariantCulture
      );
  }

  private bool WasAttempted(
    List<Node> participants,
    double tolerance)
  {
    return _attempted.Contains(
      OperationKey(participants, tolerance)
    );
  }

  // ============================================================
  // PAIR GROWTH
  //
  // For a given phase, exhaust all current-state candidates.
  // After success, immediately rebuild the candidate list.
  // ============================================================

  private int GrowPairs(
    List<Node> nodes,
    SearchPhase phase,
    double[] tolerances)
  {
    int merges = 0;

    while (HasBudget() && nodes.Count >= 2)
    {
      List<Pair> candidates =
        BuildPairCandidates(nodes);

      bool mergedThisPass = false;

      foreach (Pair pair in candidates)
      {
        if (!HasBudget())
          break;

        Node a = nodes[pair.A];
        Node b = nodes[pair.B];

        var participants =
          new List<Node> { a, b };

        Brep union = null;

        foreach (double tolerance in tolerances)
        {
          if (!HasBudget())
            break;

          union = TryUnion(
            participants,
            tolerance,
            phase
          );

          if (union != null)
            break;
        }

        if (union == null)
          continue;

        Node grown = MakeMergedNode(
          participants,
          union
        );

        if (grown == null)
        {
          _rejectedResults++;
          continue;
        }

        // Pair.A is always less than Pair.B.
        nodes.RemoveAt(pair.B);
        nodes.RemoveAt(pair.A);
        nodes.Add(grown);

        _successfulPairs++;
        merges++;
        mergedThisPass = true;

        _log.Add(
          phase.ToString().ToUpperInvariant() +
          " PAIR SUCCESS: [" +
          SourceText(a) + "] + [" +
          SourceText(b) + "] -> [" +
          SourceText(grown) + "]"
        );

        // New state: rebuild all relevant candidates.
        break;
      }

      if (!mergedThisPass)
        break;
    }

    return merges;
  }

  // ============================================================
  // DEFERRED, BOUNDED LOCAL BATCH SEARCH
  //
  // Try nearby triples only. This is deliberately less
  // exhaustive than V3's 3- and 4-solid enumeration.
  // If a triple succeeds, return immediately to pair growth.
  // ============================================================

  private bool TryOneBatchGrowth(
    List<Node> nodes)
  {
    if (nodes.Count < 3)
      return false;

    for (int seedIndex = 0;
         seedIndex < nodes.Count;
         seedIndex++)
    {
      if (!HasBudget() ||
          _batchCalls >= MAX_BATCH_CALLS)
        return false;

      Node seed = nodes[seedIndex];

      List<int> neighbors = Enumerable
        .Range(0, nodes.Count)
        .Where(i => i != seedIndex)
        .Where(
          i => BoxesMayInteract(
            seed.Box,
            nodes[i].Box
          )
        )
        .OrderByDescending(
          i => BoxOverlapScore(
            seed.Box,
            nodes[i].Box
          )
        )
        .ThenBy(i => nodes[i].FaceCount)
        .Take(MAX_BATCH_NEIGHBORS)
        .ToList();

      if (neighbors.Count < 2)
        continue;

      for (int i = 0; i < neighbors.Count; i++)
      {
        for (int j = i + 1; j < neighbors.Count; j++)
        {
          if (!HasBudget() ||
              _batchCalls >= MAX_BATCH_CALLS)
            return false;

          var selectedIndices = new List<int>
          {
            seedIndex,
            neighbors[i],
            neighbors[j]
          };

          var participants = selectedIndices
            .Select(index => nodes[index])
            .ToList();

          Brep union = TryUnion(
            participants,
            _tol,
            SearchPhase.Batch
          );

          if (union == null)
            continue;

          Node grown = MakeMergedNode(
            participants,
            union
          );

          if (grown == null)
          {
            _rejectedResults++;
            continue;
          }

          foreach (
            int index in selectedIndices
              .Distinct()
              .OrderByDescending(x => x)
          )
          {
            nodes.RemoveAt(index);
          }

          nodes.Add(grown);
          _successfulBatches++;

          _log.Add(
            "BATCH SUCCESS: " +
            string.Join(
              " + ",
              participants.Select(
                x => "[" + SourceText(x) + "]"
              )
            ) +
            " -> [" + SourceText(grown) + "]"
          );

          return true;
        }
      }
    }

    return false;
  }

  // ============================================================
  // TIMED 3D BREP BOOLEAN UNION
  //
  // The result must be a single valid closed Brep.
  // Volume and box checks reject obvious bad results,
  // but they are NOT a complete coverage proof.
  // ============================================================

  private Brep TryUnion(
    List<Node> participants,
    double tolerance,
    SearchPhase phase)
  {
    if (!HasBudget() ||
        participants == null ||
        participants.Count < 2)
      return null;

    string key = OperationKey(
      participants,
      tolerance
    );

    if (!_attempted.Add(key))
    {
      _cacheHits++;
      return null;
    }

    _booleanCalls++;

    if (phase == SearchPhase.Fast)
      _fastCalls++;
    else if (phase == SearchPhase.Retry)
      _retryCalls++;
    else
      _batchCalls++;

    Stopwatch operationWatch = Stopwatch.StartNew();

    Brep[] result = null;

    try
    {
      List<Brep> inputs = participants
        .Select(x => x.Solid)
        .ToList();

      result = Brep.CreateBooleanUnion(
        inputs,
        tolerance
      );
    }
    catch (Exception ex)
    {
      _booleanExceptions++;
      _log.Add(
        phase.ToString() +
        " Boolean exception: " + ex.Message
      );
    }
    finally
    {
      operationWatch.Stop();

      double seconds =
        operationWatch.Elapsed.TotalSeconds;

      if (phase == SearchPhase.Fast)
        _fastBooleanSeconds += seconds;
      else if (phase == SearchPhase.Retry)
        _retryBooleanSeconds += seconds;
      else
        _batchBooleanSeconds += seconds;
    }

    if (result == null || result.Length != 1)
      return null;

    Brep union = result[0];

    if (!IsUsableSolid(union))
    {
      _rejectedResults++;
      return null;
    }

    OrientOutward(union);

    double unionVolume = SolidVolume(union);

    if (double.IsNaN(unionVolume) ||
        unionVolume <= 0.0)
    {
      _rejectedResults++;
      return null;
    }

    double largestInputVolume =
      participants.Max(x => x.Volume);

    double sumInputVolume =
      participants.Sum(x => x.Volume);

    double volumeTolerance = Math.Max(
      Math.Pow(tolerance, 3.0) * 10.0,
      sumInputVolume * 1e-6
    );

    if (unionVolume + volumeTolerance <
        largestInputVolume)
    {
      _rejectedResults++;
      return null;
    }

    if (unionVolume >
        sumInputVolume + volumeTolerance)
    {
      _rejectedResults++;
      return null;
    }

    BoundingBox unionBox =
      union.GetBoundingBox(true);

    foreach (Node node in participants)
    {
      if (!BoxContains(
        unionBox,
        node.Box,
        tolerance * 3.0
      ))
      {
        _rejectedResults++;
        return null;
      }
    }

    return union;
  }

  private bool BoxContains(
    BoundingBox outer,
    BoundingBox inner,
    double tolerance)
  {
    if (!outer.IsValid || !inner.IsValid)
      return false;

    return
      outer.Min.X <= inner.Min.X + tolerance &&
      outer.Min.Y <= inner.Min.Y + tolerance &&
      outer.Min.Z <= inner.Min.Z + tolerance &&
      outer.Max.X >= inner.Max.X - tolerance &&
      outer.Max.Y >= inner.Max.Y - tolerance &&
      outer.Max.Z >= inner.Max.Z - tolerance;
  }
}
} // namespace Sliding_Simulation
