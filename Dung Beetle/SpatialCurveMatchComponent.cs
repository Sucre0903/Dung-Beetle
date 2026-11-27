using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Sliding_Simulation
{
  public sealed class SpatialCurveMatchComponent : GH_Component
  {
    private const double DefaultTolerance = 0.0;
    private const int DefaultSampleCount = 96;
    private const bool DefaultAllowFlip = true;
    private const bool DefaultUniqueTarget = true;

    public SpatialCurveMatchComponent()
      : base(
        "Spatial Curve Match",
        "SpaceMatch",
        "空间快速匹配：用平移和旋转把平面源曲线对齐到公差内全等的目标曲线。",
        "Dung Beetle",
        "Geometry")
    {
    }

    public override Guid ComponentGuid =>
      new Guid("88cbf472-f0c0-4f81-aaba-9e826fbd2d6b");

    protected override Bitmap Icon => Dung_Beetle.Properties.Resources.SpatialCurveMatchIcon;

    protected override void RegisterInputParams(GH_InputParamManager parameters)
    {
      parameters.AddCurveParameter("SourceCrvs", "S",
        "源平面曲线列表。输出顺序与此列表一致。", GH_ParamAccess.list);
      parameters.AddCurveParameter("TargetCrvs", "T",
        "候选目标平面曲线列表。目标索引在当前求解所用列表中从 0 开始。",
        GH_ParamAccess.list);
      parameters.AddNumberParameter("Tolerance", "Tol",
        "允许的最大几何偏差，单位为模型单位。0 或无效值使用 Rhino 文档绝对公差。",
        GH_ParamAccess.item, DefaultTolerance);
      parameters.AddIntegerParameter("SampleCount", "N",
        "基础采样数。小于 16 时使用 96，大于 512 时取 512。",
        GH_ParamAccess.item, DefaultSampleCount);
      parameters.AddBooleanParameter("AllowFlip", "Flip",
        "允许用反向的目标平面法向构造旋转候选。",
        GH_ParamAccess.item, DefaultAllowFlip);
      parameters.AddBooleanParameter("UniqueTarget", "Unique",
        "每次列表求解中，每个目标索引最多分配给一条源曲线。",
        GH_ParamAccess.item, DefaultUniqueTarget);

      // Let the solver report and keep alignment for empty lists.
      parameters[0].Optional = true;
      parameters[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager parameters)
    {
      parameters.AddIntegerParameter("MatchedTargetIndex", "I",
        "匹配到的目标索引，从 0 开始。未匹配时为 -1。", GH_ParamAccess.list);
      parameters.AddTransformParameter("Transforms", "X",
        "由源曲线姿态到目标姿态的刚性变换。未匹配时为无效变换。",
        GH_ParamAccess.list);
      parameters.AddBooleanParameter("IsPerfect", "OK",
        "变换后的曲线是否成功匹配且满足公差。", GH_ParamAccess.list);
      parameters.AddNumberParameter("MaxDeviation", "Dev",
        "匹配曲线的最大偏差估计值，单位为模型单位。未匹配时为 NaN。",
        GH_ParamAccess.list);
      parameters.AddCurveParameter("MatchedCrvs", "C",
        "变换后的源曲线副本。未匹配时保留空项。", GH_ParamAccess.list);
      parameters.AddTextParameter("Log", "Log",
        "本次列表求解的汇总及逐条诊断。", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
      var source = new List<GH_Curve>();
      var target = new List<GH_Curve>();
      double tolerance = DefaultTolerance;
      int sampleCount = DefaultSampleCount;
      bool allowFlip = DefaultAllowFlip;
      bool uniqueTarget = DefaultUniqueTarget;

      dataAccess.GetDataList(0, source);
      dataAccess.GetDataList(1, target);
      dataAccess.GetData(2, ref tolerance);
      dataAccess.GetData(3, ref sampleCount);
      dataAccess.GetData(4, ref allowFlip);
      dataAccess.GetData(5, ref uniqueTarget);

      if (source.Count == 0)
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "请接入至少一条源平面曲线（S）。");
      else if (target.Count == 0)
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "请接入至少一条目标平面曲线（T）。");

      CurveRigidMatcher.Match(
        source.Select(curve => curve?.Value).ToList(),
        target.Select(curve => curve?.Value).ToList(),
        tolerance,
        sampleCount,
        allowFlip,
        uniqueTarget,
        out List<int> matchedIndices,
        out List<Transform> transforms,
        out List<bool> perfectFlags,
        out List<double> deviations,
        out List<Curve> matchedCurves,
        out List<string> log);

      dataAccess.SetDataList(0, matchedIndices);
      dataAccess.SetDataList(1, transforms.Select(transform => new GH_Transform(transform)));
      dataAccess.SetDataList(2, perfectFlags);
      dataAccess.SetDataList(3, deviations.Select(deviation => new GH_Number(deviation)));
      dataAccess.SetDataList(4, matchedCurves);
      dataAccess.SetDataList(5, log);
    }
  }
}
