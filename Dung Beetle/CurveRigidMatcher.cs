using System;
using System.Collections.Generic;
using System.Linq;

using Rhino;
using Rhino.Geometry;

namespace Sliding_Simulation
{
  internal static class CurveRigidMatcher
  {
    // ============================================================
    // Curve Rigid Matcher
    //
    // Purpose:
    //   Match every Source curve to a geometrically congruent Target
    //   using ONLY rigid transformations:
    //       Translation + Rotation
    //
    //   NO scale
    //   NO shear
    //   NO deformation
    //
    // Ported from the Rhino 8 Grasshopper C# script, transform V1.
    // ============================================================


    private class CurveInfo
    {
      public int Index = -1;
      public Curve Curve = null;

      public bool Valid = false;
      public string Error = "";

      public bool Closed = false;
      public double Length = 0.0;

      public Plane Plane = Plane.Unset;

      public Point3d Centroid = Point3d.Unset;
      public bool HasCentroid = false;
      public bool ApproxCentroid = false;

      public bool HasArea = false;
      public double Area = 0.0;

      public List<Point3d> Anchors = new List<Point3d>();
    }


    private class TransformCandidate
    {
      public Transform XForm = Transform.Unset;
      public bool Flipped = false;
      public string Method = "";
    }


    private class PairResult
    {
      public int SourceIndex = -1;
      public int TargetIndex = -1;

      public Transform XForm = Transform.Unset;

      public double Deviation = double.PositiveInfinity;
      public double LengthError = double.PositiveInfinity;
      public double AreaError = double.NaN;

      public bool Perfect = false;
      public bool Flipped = false;

      public string Method = "";
    }


    internal static void Match(
      List<Curve> SourceCrvs,
      List<Curve> TargetCrvs,
      double Tolerance,
      int SampleCount,
      bool AllowFlip,
      bool UniqueTarget,
      out List<int> MatchedTargetIndex,
      out List<Transform> Transforms,
      out List<bool> IsPerfect,
      out List<double> MaxDeviation,
      out List<Curve> MatchedCrvs,
      out List<string> Log)
    {
      // ------------------------------------------------------------
      // 0. Input sanitizing
      // ------------------------------------------------------------

      if (SourceCrvs == null)
        SourceCrvs = new List<Curve>();

      if (TargetCrvs == null)
        TargetCrvs = new List<Curve>();


      double tol = Tolerance;

      if (!IsFinite(tol) || tol <= 0.0)
      {
        RhinoDoc doc = RhinoDoc.ActiveDoc;

        if (doc != null)
          tol = doc.ModelAbsoluteTolerance;
        else
          tol = 0.001;
      }


      int samples = SampleCount;

      if (samples < 16)
        samples = 96;

      if (samples > 512)
        samples = 512;


      int sourceCount = SourceCrvs.Count;
      int targetCount = TargetCrvs.Count;


      // ------------------------------------------------------------
      // 1. Analyze curves once
      // ------------------------------------------------------------

      List<CurveInfo> sourceInfos = new List<CurveInfo>();
      List<CurveInfo> targetInfos = new List<CurveInfo>();


      for (int i = 0; i < sourceCount; i++)
        sourceInfos.Add(BuildCurveInfo(SourceCrvs[i], i, tol, samples));


      for (int i = 0; i < targetCount; i++)
        targetInfos.Add(BuildCurveInfo(TargetCrvs[i], i, tol, samples));


      // ------------------------------------------------------------
      // 2. Test Source / Target pairs
      // ------------------------------------------------------------

      List<PairResult>[] perfectEdges = new List<PairResult>[sourceCount];

      PairResult[] bestAttempts = new PairResult[sourceCount];


      for (int i = 0; i < sourceCount; i++)
      {
        perfectEdges[i] = new List<PairResult>();

        CurveInfo s = sourceInfos[i];

        if (!s.Valid)
          continue;


        for (int j = 0; j < targetCount; j++)
        {
          CurveInfo t = targetInfos[j];

          if (!t.Valid)
            continue;


          PairResult result = EvaluatePair(
            s,
            t,
            tol,
            samples,
            AllowFlip
          );


          if (result == null)
            continue;


          // Keep best attempted match for diagnostic log
          if (
            bestAttempts[i] == null ||
            result.Deviation < bestAttempts[i].Deviation
          )
          {
            bestAttempts[i] = result;
          }


          if (result.Perfect)
            perfectEdges[i].Add(result);
        }


        perfectEdges[i] = perfectEdges[i]
          .OrderBy(x => x.Deviation)
          .ThenBy(x => x.TargetIndex)
          .ToList();
      }


      // ------------------------------------------------------------
      // 3. One-to-one assignment
      // ------------------------------------------------------------

      PairResult[] selected = new PairResult[sourceCount];


      if (UniqueTarget)
      {
        int[] targetOwner = Enumerable
          .Repeat(-1, targetCount)
          .ToArray();


        // Sources with fewer options are matched first.
        // This greatly reduces unnecessary conflicts.
        List<int> sourceOrder = Enumerable
          .Range(0, sourceCount)
          .Where(i => perfectEdges[i].Count > 0)
          .OrderBy(i => perfectEdges[i].Count)
          .ThenBy(i => i)
          .ToList();


        foreach (int sourceIndex in sourceOrder)
        {
          bool[] visitedTargets = new bool[targetCount];

          TryAugment(
            sourceIndex,
            perfectEdges,
            visitedTargets,
            targetOwner,
            selected
          );
        }
      }
      else
      {
        for (int i = 0; i < sourceCount; i++)
        {
          if (perfectEdges[i].Count > 0)
            selected[i] = perfectEdges[i][0];
        }
      }


      // ------------------------------------------------------------
      // 4. Prepare outputs
      // ------------------------------------------------------------

      List<int> matchedIndices =
        Enumerable.Repeat(-1, sourceCount).ToList();

      List<Transform> transforms =
        Enumerable.Repeat(Transform.Unset, sourceCount).ToList();

      List<bool> perfectFlags =
        Enumerable.Repeat(false, sourceCount).ToList();

      List<double> deviations =
        Enumerable.Repeat(double.NaN, sourceCount).ToList();

      List<Curve> matchedCurves =
        Enumerable.Repeat<Curve>(null, sourceCount).ToList();

      List<string> logs = new List<string>();


      int successCount = 0;


      for (int i = 0; i < sourceCount; i++)
      {
        CurveInfo s = sourceInfos[i];


        // Invalid Source
        if (!s.Valid)
        {
          logs.Add(
            "Source[" + i + "]\n" +
            "Result          : INVALID SOURCE\n" +
            "Reason          : " + s.Error
          );

          continue;
        }


        PairResult chosen = selected[i];


        // ----------------------------------------------------------
        // PERFECT MATCH
        // ----------------------------------------------------------

        if (chosen != null)
        {
          Curve moved = s.Curve.DuplicateCurve();
          if (moved == null || !moved.Transform(chosen.XForm) || !moved.IsValid)
          {
            moved?.Dispose();
            logs.Add(
              "Source[" + i + "]\n" +
              "Target          : " + chosen.TargetIndex + "\n" +
              "Result          : TRANSFORM FAILED"
            );
            continue;
          }

          successCount++;
          matchedIndices[i] = chosen.TargetIndex;
          transforms[i] = chosen.XForm;
          perfectFlags[i] = true;
          deviations[i] = chosen.Deviation;
          matchedCurves[i] = moved;


          bool rigid = IsRigidNoScale(chosen.XForm);

          double scale = AverageTransformScale(chosen.XForm);


          string ambiguity;

          if (perfectEdges[i].Count <= 1)
          {
            ambiguity = "Unique";
          }
          else
          {
            string ids = string.Join(
              ", ",
              perfectEdges[i]
              .Select(x => x.TargetIndex.ToString())
            );

            ambiguity =
              "AMBIGUOUS / candidates = [" + ids + "]";
          }


          logs.Add(
            "Source[" + i + "]\n" +
            "Target          : " + chosen.TargetIndex + "\n" +
            "Result          : PERFECT\n" +
            "Assignment      : " + ambiguity + "\n" +
            "Method          : " + chosen.Method + "\n" +
            "Closed          : " + s.Closed + "\n" +
            "PlaneFlipTest   : " + chosen.Flipped + "\n" +
            "LengthError     : " + Num(chosen.LengthError) + "\n" +
            "AreaError       : " + Num(chosen.AreaError) + "\n" +
            "MaxDeviation    : " + Num(chosen.Deviation) + "\n" +
            "Tolerance       : " + Num(tol) + "\n" +
            "RigidTransform  : " + rigid + "\n" +
            "Scale           : " + scale.ToString("G12")
          );
        }

        // ----------------------------------------------------------
        // NO PERFECT MATCH
        // ----------------------------------------------------------

        else
        {
          PairResult best = bestAttempts[i];


          if (perfectEdges[i].Count > 0)
          {
            string ids = string.Join(
              ", ",
              perfectEdges[i]
              .Select(x => x.TargetIndex.ToString())
            );


            logs.Add(
              "Source[" + i + "]\n" +
              "Result          : UNASSIGNED\n" +
              "Reason          : Perfect target exists, but target is already assigned\n" +
              "PerfectTargets  : [" + ids + "]\n" +
              "UniqueTarget    : " + UniqueTarget
            );
          }
          else if (best != null)
          {
            logs.Add(
              "Source[" + i + "]\n" +
              "Result          : NO PERFECT MATCH\n" +
              "BestTarget      : " + best.TargetIndex + "\n" +
              "BestMethod      : " + best.Method + "\n" +
              "BestDeviation   : " + Num(best.Deviation) + "\n" +
              "Tolerance       : " + Num(tol) + "\n" +
              "LengthError     : " + Num(best.LengthError) + "\n" +
              "AreaError       : " + Num(best.AreaError) + "\n" +
              "Reason          : Geometry does not overlap within tolerance"
            );
          }
          else
          {
            logs.Add(
              "Source[" + i + "]\n" +
              "Result          : NO MATCH\n" +
              "Reason          : No compatible target passed basic geometric filters"
            );
          }
        }
      }


      int invalidSources =
        sourceInfos.Count(x => !x.Valid);

      int invalidTargets =
        targetInfos.Count(x => !x.Valid);


      logs.Insert(
        0,
        "[SUMMARY]\n" +
        "SourceCount      : " + sourceCount + "\n" +
        "TargetCount      : " + targetCount + "\n" +
        "PerfectMatched   : " + successCount + "\n" +
        "Unmatched        : " + (sourceCount - successCount) + "\n" +
        "InvalidSources   : " + invalidSources + "\n" +
        "InvalidTargets   : " + invalidTargets + "\n" +
        "Tolerance        : " + Num(tol) + "\n" +
        "SampleCount      : " + samples + "\n" +
        "AllowFlip        : " + AllowFlip + "\n" +
        "UniqueTarget     : " + UniqueTarget
      );


      // ------------------------------------------------------------
      // 5. Output
      // ------------------------------------------------------------

      MatchedTargetIndex = matchedIndices;
      Transforms = transforms;
      IsPerfect = perfectFlags;
      MaxDeviation = deviations;
      MatchedCrvs = matchedCurves;
      Log = logs;
    }



    // ============================================================
    // CURVE PREPROCESS
    // ============================================================

    private static CurveInfo BuildCurveInfo(
      Curve curve,
      int index,
      double tol,
      int sampleCount)
    {
      CurveInfo info = new CurveInfo();

      info.Index = index;
      info.Curve = curve;


      if (curve == null)
      {
        info.Error = "Curve is null.";
        return info;
      }


      if (!curve.IsValid)
      {
        info.Error = "Curve is invalid.";
        return info;
      }


      Plane plane;

      if (!curve.TryGetPlane(out plane, tol))
      {
        info.Error =
          "Curve is not planar within tolerance " + Num(tol);

        return info;
      }


      double length;

      try
      {
        length = curve.GetLength();
      }
      catch
      {
        info.Error = "Failed to calculate curve length.";
        return info;
      }


      if (!IsFinite(length) || length <= 1e-12)
      {
        info.Error = "Curve length is zero or invalid.";
        return info;
      }


      info.Closed = curve.IsClosed;
      info.Length = length;
      info.Plane = plane;


      // ----------------------------------------------------------
      // Length centroid
      // This is independent of:
      //   position
      //   rotation
      //   curve domain
      //   closed curve seam
      // ----------------------------------------------------------

      Point3d centroid;

      if (TryLengthCentroid(curve, out centroid))
      {
        info.Centroid = centroid;
        info.HasCentroid = true;
        info.ApproxCentroid = false;
      }
      else
      {
        centroid = ApproximateLengthCentroid(
          curve,
          Math.Max(128, sampleCount * 2)
        );

        if (centroid.IsValid)
        {
          info.Centroid = centroid;
          info.HasCentroid = true;
          info.ApproxCentroid = true;
        }
      }


      // ----------------------------------------------------------
      // Area is only an optional fast filter.
      // Final match NEVER depends on area alone.
      // ----------------------------------------------------------

      if (info.Closed)
      {
        double area;

        if (TryArea(curve, tol, out area))
        {
          info.HasArea = true;
          info.Area = Math.Abs(area);
        }


        if (!info.HasCentroid)
        {
          info.Error =
            "Could not calculate a stable centroid for closed curve.";

          return info;
        }


        info.Anchors = FindClosedAnchors(
          curve,
          info.Centroid,
          sampleCount,
          tol
        );


        if (info.Anchors.Count == 0)
          info.Anchors.Add(curve.PointAtStart);
      }


      info.Valid = true;
      return info;
    }



    // ============================================================
    // PAIR MATCHING
    // ============================================================

    private static PairResult EvaluatePair(
      CurveInfo source,
      CurveInfo target,
      double tol,
      int sampleCount,
      bool allowFlip)
    {
      if (source == null || target == null)
        return null;

      if (!source.Valid || !target.Valid)
        return null;


      // Open must match open, closed must match closed.
      if (source.Closed != target.Closed)
        return null;


      // ----------------------------------------------------------
      // Length invariant
      // ----------------------------------------------------------

      double lengthError =
        Math.Abs(source.Length - target.Length);

      double maxLength =
        Math.Max(source.Length, target.Length);

      double lengthGate =
        Math.Max(
          tol * 10.0,
          maxLength * 1e-8
        );


      if (lengthError > lengthGate)
        return null;


      // ----------------------------------------------------------
      // Area invariant for closed curves
      // ----------------------------------------------------------

      double areaError = double.NaN;


      if (
        source.Closed &&
        source.HasArea &&
        target.HasArea
      )
      {
        areaError =
          Math.Abs(source.Area - target.Area);

        double maxArea =
          Math.Max(source.Area, target.Area);

        double areaGate =
          Math.Max(
            tol * (source.Length + target.Length) * 4.0,
            Math.Max(
              maxArea * 1e-7,
              tol * tol * 4.0
            )
          );


        if (areaError > areaGate)
          return null;
      }


      // ----------------------------------------------------------
      // Open curve end-to-end chord is another rigid invariant.
      // ----------------------------------------------------------

      if (!source.Closed)
      {
        double sChord =
          source.Curve.PointAtStart.DistanceTo(
            source.Curve.PointAtEnd
          );

        double tChord =
          target.Curve.PointAtStart.DistanceTo(
            target.Curve.PointAtEnd
          );


        double chordError =
          Math.Abs(sChord - tChord);

        double chordGate =
          Math.Max(
            tol * 5.0,
            Math.Max(sChord, tChord) * 1e-8
          );


        if (chordError > chordGate)
          return null;
      }


      // ----------------------------------------------------------
      // Generate rigid-transform candidates
      // ----------------------------------------------------------

      List<TransformCandidate> candidates;

      if (source.Closed)
      {
        candidates = MakeClosedCandidates(
          source,
          target,
          tol,
          allowFlip
        );
      }
      else
      {
        candidates = MakeOpenCandidates(
          source,
          target,
          allowFlip
        );
      }


      if (candidates.Count == 0)
        return null;


      PairResult best = null;


      // ----------------------------------------------------------
      // Actual geometry verification
      // ----------------------------------------------------------

      foreach (TransformCandidate candidate in candidates)
      {
        if (!candidate.XForm.IsValid)
          continue;


        // Mandatory:
        // Reject Scale / Shear / non-rigid transformations.
        if (!IsRigidNoScale(candidate.XForm))
          continue;


        double deviation = ComputeDeviation(
          source.Curve,
          target.Curve,
          candidate.XForm,
          tol,
          sampleCount
        );


        if (!IsFinite(deviation))
          continue;


        if (
          best == null ||
          deviation < best.Deviation
        )
        {
          best = new PairResult();

          best.SourceIndex = source.Index;
          best.TargetIndex = target.Index;

          best.XForm = candidate.XForm;

          best.Deviation = deviation;
          best.LengthError = lengthError;
          best.AreaError = areaError;

          best.Flipped = candidate.Flipped;
          best.Method = candidate.Method;

          best.Perfect = deviation <= tol;
        }


        // Exact / near machine-precision result.
        // No point testing more equivalent orientations.
        double earlyStop =
          Math.Max(tol * 0.01, 1e-10);

        if (
          best != null &&
          best.Deviation <= earlyStop
        )
        {
          break;
        }
      }


      if (best != null)
        best.Perfect = best.Deviation <= tol;


      return best;
    }



    // ============================================================
    // OPEN CURVE TRANSFORM CANDIDATES
    // ============================================================

    private static List<TransformCandidate> MakeOpenCandidates(
      CurveInfo source,
      CurveInfo target,
      bool allowFlip)
    {
      List<TransformCandidate> result =
        new List<TransformCandidate>();


      Point3d s0 = source.Curve.PointAtStart;
      Point3d s1 = source.Curve.PointAtEnd;

      Point3d t0 = target.Curve.PointAtStart;
      Point3d t1 = target.Curve.PointAtEnd;


      Vector3d sx = s1 - s0;


      // Very small start/end chord:
      // use tangent instead.
      if (sx.SquareLength <= 1e-20)
        sx = source.Curve.TangentAtStart;


      Plane sourceFrame;

      if (
        !TryMakeFrame(
          s0,
          sx,
          source.Plane.Normal,
          out sourceFrame
        )
      )
      {
        return result;
      }


      int flipCases = allowFlip ? 2 : 1;


      // Two correspondence cases:
      //
      // 0:
      // Source Start -> Target Start
      //
      // 1:
      // Source Start -> Target End
      //
      for (int reversed = 0; reversed < 2; reversed++)
      {
        Point3d targetOrigin;
        Vector3d tx;


        if (reversed == 0)
        {
          targetOrigin = t0;
          tx = t1 - t0;

          if (tx.SquareLength <= 1e-20)
            tx = target.Curve.TangentAtStart;
        }
        else
        {
          targetOrigin = t1;
          tx = t0 - t1;

          if (tx.SquareLength <= 1e-20)
            tx = -target.Curve.TangentAtEnd;
        }


        for (int flip = 0; flip < flipCases; flip++)
        {
          Vector3d targetNormal =
            target.Plane.Normal;


          if (flip == 1)
            targetNormal = -targetNormal;


          Plane targetFrame;

          if (
            !TryMakeFrame(
              targetOrigin,
              tx,
              targetNormal,
              out targetFrame
            )
          )
          {
            continue;
          }


          Transform xform =
            Transform.PlaneToPlane(
              sourceFrame,
              targetFrame
            );


          if (!xform.IsValid)
            continue;


          TransformCandidate c =
            new TransformCandidate();

          c.XForm = xform;
          c.Flipped = flip == 1;

          c.Method =
            reversed == 0
            ? "Open / Start->Start"
            : "Open / Start->End";


          result.Add(c);
        }
      }


      return result;
    }



    // ============================================================
    // CLOSED CURVE TRANSFORM CANDIDATES
    // ============================================================

    private static List<TransformCandidate> MakeClosedCandidates(
      CurveInfo source,
      CurveInfo target,
      double tol,
      bool allowFlip)
    {
      List<TransformCandidate> result =
        new List<TransformCandidate>();


      if (
        !source.HasCentroid ||
        !target.HasCentroid
      )
      {
        return result;
      }


      List<Point3d> sourceAnchors =
        source.Anchors;

      List<Point3d> targetAnchors =
        target.Anchors;


      if (
        sourceAnchors == null ||
        sourceAnchors.Count == 0
      )
      {
        sourceAnchors =
          new List<Point3d>()
          {
            source.Curve.PointAtStart
          };
      }


      if (
        targetAnchors == null ||
        targetAnchors.Count == 0
      )
      {
        targetAnchors =
          new List<Point3d>()
          {
            target.Curve.PointAtStart
          };
      }


      int flipCases = allowFlip ? 2 : 1;


      foreach (Point3d sa in sourceAnchors)
      {
        Vector3d sourceRadial =
          sa - source.Centroid;

        double sourceRadius =
          sourceRadial.Length;


        if (sourceRadius <= 1e-12)
          continue;


        Plane sourceFrame;

        if (
          !TryMakeFrame(
            source.Centroid,
            sourceRadial,
            source.Plane.Normal,
            out sourceFrame
          )
        )
        {
          continue;
        }


        foreach (Point3d ta in targetAnchors)
        {
          Vector3d targetRadial =
            ta - target.Centroid;

          double targetRadius =
            targetRadial.Length;


          if (targetRadius <= 1e-12)
            continue;


          // Farthest-radius distance is itself a rigid invariant.
          double radiusError =
            Math.Abs(sourceRadius - targetRadius);

          double radiusGate =
            Math.Max(
              tol * 8.0,
              Math.Max(
                sourceRadius,
                targetRadius
              ) * 1e-7
            );


          if (radiusError > radiusGate)
            continue;


          for (int flip = 0; flip < flipCases; flip++)
          {
            Vector3d targetNormal =
              target.Plane.Normal;


            if (flip == 1)
              targetNormal = -targetNormal;


            Plane targetFrame;

            if (
              !TryMakeFrame(
                target.Centroid,
                targetRadial,
                targetNormal,
                out targetFrame
              )
            )
            {
              continue;
            }


            Transform xform =
              Transform.PlaneToPlane(
                sourceFrame,
                targetFrame
              );


            if (!xform.IsValid)
              continue;


            TransformCandidate c =
              new TransformCandidate();

            c.XForm = xform;
            c.Flipped = flip == 1;

            c.Method =
              "Closed / LengthCentroid + RadialAnchor";

            result.Add(c);
          }
        }
      }


      return result;
    }



    // ============================================================
    // CLOSED CURVE ANCHOR SEARCH
    //
    // Find geometry-defined points that do NOT depend on the seam.
    //
    // Main anchor:
    //   farthest point(s) from the curve length centroid.
    //
    // We combine:
    //   - local perpendicular-point solutions
    //   - exact polyline vertices
    //   - exploded segment endpoints
    //   - arc-length samples
    //
    // This handles:
    //   Circle
    //   Ellipse
    //   Polyline
    //   PolyCurve
    //   NurbsCurve
    // ============================================================

    private static List<Point3d> FindClosedAnchors(
      Curve curve,
      Point3d center,
      int sampleCount,
      double tol)
    {
      List<Point3d> candidates =
        new List<Point3d>();


      double dedupTol =
        Math.Max(tol * 0.25, 1e-10);


      AddUniquePoint(
        candidates,
        curve.PointAtStart,
        dedupTol
      );


      // ----------------------------------------------------------
      // Exact polyline vertices
      // ----------------------------------------------------------

      try
      {
        Polyline polyline;

        if (curve.TryGetPolyline(out polyline))
        {
          for (int i = 0; i < polyline.Count; i++)
          {
            AddUniquePoint(
              candidates,
              polyline[i],
              dedupTol
            );
          }
        }
      }
      catch
      {
      }


      // ----------------------------------------------------------
      // Segment endpoints / kinks
      // ----------------------------------------------------------

      try
      {
        Curve[] segments =
          curve.DuplicateSegments();


        if (segments != null)
        {
          foreach (Curve segment in segments)
          {
            if (segment == null)
              continue;

            AddUniquePoint(
              candidates,
              segment.PointAtStart,
              dedupTol
            );

            AddUniquePoint(
              candidates,
              segment.PointAtEnd,
              dedupTol
            );
          }
        }
      }
      catch
      {
      }


      // ----------------------------------------------------------
      // Arc-length sampling + local perpendicular points
      // ----------------------------------------------------------

      int n = sampleCount;

      if (n < 32)
        n = 32;

      if (n > 192)
        n = 192;


      Point3d[] samplePoints =
        new Point3d[n];

      double[] sampleRadius =
        new double[n];


      double minRadius =
        double.PositiveInfinity;

      double maxRadius =
        double.NegativeInfinity;


      for (int i = 0; i < n; i++)
      {
        double u =
          (double)i / (double)n;


        double seedT;

        Point3d p;


        if (
          curve.NormalizedLengthParameter(
            u,
            out seedT
          )
        )
        {
          p = curve.PointAt(seedT);
        }
        else
        {
          p = curve.PointAtNormalizedLength(u);
          seedT = curve.Domain.ParameterAt(u);
        }


        samplePoints[i] = p;

        double radius =
          p.DistanceTo(center);

        sampleRadius[i] = radius;


        if (radius < minRadius)
          minRadius = radius;

        if (radius > maxRadius)
          maxRadius = radius;


        // Search for true perpendicular point near this seed.
        try
        {
          double perpT;

          if (
            curve.GetLocalPerpPoint(
              center,
              seedT,
              out perpT
            )
          )
          {
            Point3d q =
              curve.PointAt(perpT);

            AddUniquePoint(
              candidates,
              q,
              dedupTol
            );
          }
        }
        catch
        {
        }
      }


      // ----------------------------------------------------------
      // Circle / constant-radius curve case
      //
      // For a circle every point is an equivalent valid anchor.
      // One point is enough.
      // ----------------------------------------------------------

      double flatTol =
        Math.Max(
          tol * 4.0,
          Math.Max(1.0, maxRadius) * 1e-9
        );


      if (
        IsFinite(minRadius) &&
        IsFinite(maxRadius) &&
        Math.Abs(maxRadius - minRadius) <= flatTol
      )
      {
        return new List<Point3d>()
        {
          curve.PointAtStart
        };
      }


      // ----------------------------------------------------------
      // Add sampled local maxima
      // Important for sharp corners where tangent-based solving can
      // be ambiguous.
      // ----------------------------------------------------------

      double localEps =
        Math.Max(tol * 0.1, 1e-12);


      for (int i = 0; i < n; i++)
      {
        int prev =
          (i - 1 + n) % n;

        int next =
          (i + 1) % n;


        if (
          sampleRadius[i] >=
            sampleRadius[prev] - localEps &&
          sampleRadius[i] >=
            sampleRadius[next] - localEps
        )
        {
          AddUniquePoint(
            candidates,
            samplePoints[i],
            dedupTol
          );
        }
      }


      if (candidates.Count == 0)
      {
        return new List<Point3d>()
        {
          curve.PointAtStart
        };
      }


      // ----------------------------------------------------------
      // Keep only the farthest candidate group.
      // ----------------------------------------------------------

      double candidateMax =
        double.NegativeInfinity;


      foreach (Point3d p in candidates)
      {
        double r =
          p.DistanceTo(center);

        if (r > candidateMax)
          candidateMax = r;
      }


      double radiusBand =
        Math.Max(
          tol * 8.0,
          Math.Max(
            1.0,
            candidateMax
          ) * 1e-7
        );


      List<Point3d> ranked =
        candidates
        .OrderByDescending(
          p => p.DistanceTo(center)
        )
        .ToList();


      List<Point3d> result =
        new List<Point3d>();


      foreach (Point3d p in ranked)
      {
        double r =
          p.DistanceTo(center);


        if (r < candidateMax - radiusBand)
          continue;


        AddUniquePoint(
          result,
          p,
          dedupTol
        );


        // Safety cap.
        // 24 equivalent extreme anchors is already more than enough
        // for practical curve matching.
        if (result.Count >= 24)
          break;
      }


      if (result.Count == 0)
        result.Add(ranked[0]);


      return result;
    }



    // ============================================================
    // FINAL GEOMETRY DEVIATION
    // ============================================================

    private static double ComputeDeviation(
      Curve source,
      Curve target,
      Transform xform,
      double tol,
      int sampleCount)
    {
      if (
        source == null ||
        target == null ||
        !xform.IsValid
      )
      {
        return double.PositiveInfinity;
      }


      using (Curve moved = source.DuplicateCurve())
      {
        if (moved == null)
          return double.PositiveInfinity;


        bool transformed =
          moved.Transform(xform);


        if (!transformed)
          return double.PositiveInfinity;


        // ----------------------------------------------------------
        // RhinoCommon curve-deviation solver
        // ----------------------------------------------------------

        double exactDeviation =
          double.PositiveInfinity;


        try
        {
          double maxDistance;
          double maxDistanceParameterA;
          double maxDistanceParameterB;

          double minDistance;
          double minDistanceParameterA;
          double minDistanceParameterB;


          double deviationTolerance =
            Math.Max(tol * 0.25, 1e-9);


          bool ok =
            Curve.GetDistancesBetweenCurves(
              moved,
              target,
              deviationTolerance,
              out maxDistance,
              out maxDistanceParameterA,
              out maxDistanceParameterB,
              out minDistance,
              out minDistanceParameterA,
              out minDistanceParameterB
            );


          if (
            ok &&
            IsFinite(maxDistance)
          )
          {
            exactDeviation = maxDistance;
          }
        }
        catch
        {
        }


        // ----------------------------------------------------------
        // Independent bidirectional sample verification.
        //
        // This deliberately checks:
        //   moved Source -> Target
        //   Target -> moved Source
        //
        // so a partial-overlap situation cannot pass easily.
        // ----------------------------------------------------------

        int verifySamples =
          Math.Max(
            32,
            Math.Min(sampleCount * 2, 512)
          );


        double sampledDeviation =
          SampleHausdorffDistance(
            moved,
            target,
            verifySamples
          );


        if (
          IsFinite(exactDeviation) &&
          IsFinite(sampledDeviation)
        )
        {
          return Math.Max(
            exactDeviation,
            sampledDeviation
          );
        }


        if (IsFinite(exactDeviation))
          return exactDeviation;


        return sampledDeviation;
      }
    }



    private static double SampleHausdorffDistance(
      Curve a,
      Curve b,
      int samples)
    {
      double ab =
        DirectedSampleDistance(
          a,
          b,
          samples
        );


      double ba =
        DirectedSampleDistance(
          b,
          a,
          samples
        );


      return Math.Max(ab, ba);
    }



    private static double DirectedSampleDistance(
      Curve source,
      Curve target,
      int samples)
    {
      if (
        source == null ||
        target == null
      )
      {
        return double.PositiveInfinity;
      }


      if (samples < 2)
        samples = 2;


      bool closed =
        source.IsClosed;


      double maxDistance = 0.0;
      bool foundAny = false;


      for (int i = 0; i < samples; i++)
      {
        double u;


        if (closed)
        {
          u =
            (double)i /
            (double)samples;
        }
        else
        {
          u =
            (double)i /
            (double)(samples - 1);
        }


        Point3d p =
          source.PointAtNormalizedLength(u);


        if (!p.IsValid)
          continue;


        double t;


        if (!target.ClosestPoint(p, out t))
          continue;


        Point3d q =
          target.PointAt(t);


        if (!q.IsValid)
          continue;


        double d =
          p.DistanceTo(q);


        foundAny = true;


        if (d > maxDistance)
          maxDistance = d;
      }


      if (!foundAny)
        return double.PositiveInfinity;


      return maxDistance;
    }



    // ============================================================
    // RIGID TRANSFORM VALIDATION
    //
    // Verify matrix contains:
    //   orthonormal axes
    //   scale = 1
    //   no shear
    //   no perspective
    //
    // determinant must be +1.
    // ============================================================

    private static bool IsRigidNoScale(
      Transform xform)
    {
      if (!xform.IsValid)
        return false;


      const double eps = 1e-7;


      // Affine last row
      if (
        Math.Abs(xform.M30) > eps ||
        Math.Abs(xform.M31) > eps ||
        Math.Abs(xform.M32) > eps ||
        Math.Abs(xform.M33 - 1.0) > eps
      )
      {
        return false;
      }


      Vector3d x =
        new Vector3d(
          xform.M00,
          xform.M10,
          xform.M20
        );

      Vector3d y =
        new Vector3d(
          xform.M01,
          xform.M11,
          xform.M21
        );

      Vector3d z =
        new Vector3d(
          xform.M02,
          xform.M12,
          xform.M22
        );


      if (
        Math.Abs(x.Length - 1.0) > eps ||
        Math.Abs(y.Length - 1.0) > eps ||
        Math.Abs(z.Length - 1.0) > eps
      )
      {
        return false;
      }


      if (
        Math.Abs(Dot(x, y)) > eps ||
        Math.Abs(Dot(y, z)) > eps ||
        Math.Abs(Dot(z, x)) > eps
      )
      {
        return false;
      }


      double determinant =
          xform.M00 *
          (
            xform.M11 * xform.M22 -
            xform.M12 * xform.M21
          )
        -
          xform.M01 *
          (
            xform.M10 * xform.M22 -
            xform.M12 * xform.M20
          )
        +
          xform.M02 *
          (
            xform.M10 * xform.M21 -
            xform.M11 * xform.M20
          );


      if (
        Math.Abs(determinant - 1.0) >
        1e-6
      )
      {
        return false;
      }


      return true;
    }



    private static double AverageTransformScale(
      Transform xform)
    {
      Vector3d x =
        new Vector3d(
          xform.M00,
          xform.M10,
          xform.M20
        );

      Vector3d y =
        new Vector3d(
          xform.M01,
          xform.M11,
          xform.M21
        );

      Vector3d z =
        new Vector3d(
          xform.M02,
          xform.M12,
          xform.M22
        );


      return (
        x.Length +
        y.Length +
        z.Length
      ) / 3.0;
    }



    // ============================================================
    // FRAME CONSTRUCTION
    // ============================================================

    private static bool TryMakeFrame(
      Point3d origin,
      Vector3d xDirection,
      Vector3d normal,
      out Plane frame)
    {
      frame = Plane.Unset;


      if (!origin.IsValid)
        return false;


      Vector3d n = normal;

      if (!n.Unitize())
        return false;


      Vector3d x = xDirection;


      // Remove any tiny component normal to plane.
      double dot = Dot(x, n);


      x =
        new Vector3d(
          x.X - dot * n.X,
          x.Y - dot * n.Y,
          x.Z - dot * n.Z
        );


      if (!x.Unitize())
        return false;


      Vector3d y =
        Vector3d.CrossProduct(n, x);


      if (!y.Unitize())
        return false;


      Plane p =
        new Plane(
          origin,
          x,
          y
        );


      if (!p.IsValid)
        return false;


      frame = p;

      return true;
    }



    // ============================================================
    // ONE-TO-ONE BIPARTITE MATCHING
    // ============================================================

    private static bool TryAugment(
      int sourceIndex,
      List<PairResult>[] edges,
      bool[] visitedTargets,
      int[] targetOwner,
      PairResult[] selected)
    {
      List<PairResult> options =
        edges[sourceIndex];


      foreach (PairResult edge in options)
      {
        int targetIndex =
          edge.TargetIndex;


        if (
          targetIndex < 0 ||
          targetIndex >= targetOwner.Length
        )
        {
          continue;
        }


        if (visitedTargets[targetIndex])
          continue;


        visitedTargets[targetIndex] = true;


        int oldSource =
          targetOwner[targetIndex];


        if (
          oldSource < 0 ||
          TryAugment(
            oldSource,
            edges,
            visitedTargets,
            targetOwner,
            selected
          )
        )
        {
          targetOwner[targetIndex] =
            sourceIndex;

          selected[sourceIndex] =
            edge;

          return true;
        }
      }


      return false;
    }



    // ============================================================
    // MASS PROPERTIES
    // ============================================================

    private static bool TryLengthCentroid(
      Curve curve,
      out Point3d centroid)
    {
      centroid =
        Point3d.Unset;


      LengthMassProperties mp = null;


      try
      {
        mp =
          LengthMassProperties.Compute(curve);


        if (mp == null)
          return false;


        Point3d c =
          mp.Centroid;


        if (!c.IsValid)
          return false;


        centroid = c;

        return true;
      }
      catch
      {
        return false;
      }
      finally
      {
        if (mp != null)
          mp.Dispose();
      }
    }



    private static bool TryArea(
      Curve curve,
      double tol,
      out double area)
    {
      area = 0.0;


      if (
        curve == null ||
        !curve.IsClosed
      )
      {
        return false;
      }


      AreaMassProperties mp = null;


      try
      {
        mp =
          AreaMassProperties.Compute(
            curve,
            tol
          );


        if (mp == null)
          return false;


        double a =
          mp.Area;


        if (!IsFinite(a))
          return false;


        area = a;

        return true;
      }
      catch
      {
        return false;
      }
      finally
      {
        if (mp != null)
          mp.Dispose();
      }
    }



    private static Point3d ApproximateLengthCentroid(
      Curve curve,
      int samples)
    {
      if (curve == null)
        return Point3d.Unset;


      if (samples < 32)
        samples = 32;


      double sx = 0.0;
      double sy = 0.0;
      double sz = 0.0;

      int count = 0;


      // Midpoint sampling avoids duplicating an open endpoint
      // and gives equal arc-length weighting.
      for (int i = 0; i < samples; i++)
      {
        double u =
          (
            (double)i + 0.5
          ) /
          (double)samples;


        Point3d p =
          curve.PointAtNormalizedLength(u);


        if (!p.IsValid)
          continue;


        sx += p.X;
        sy += p.Y;
        sz += p.Z;

        count++;
      }


      if (count == 0)
        return Point3d.Unset;


      return new Point3d(
        sx / count,
        sy / count,
        sz / count
      );
    }



    // ============================================================
    // SMALL HELPERS
    // ============================================================

    private static void AddUniquePoint(
      List<Point3d> points,
      Point3d point,
      double tolerance)
    {
      if (
        points == null ||
        !point.IsValid
      )
      {
        return;
      }


      foreach (Point3d existing in points)
      {
        if (
          existing.DistanceTo(point) <=
          tolerance
        )
        {
          return;
        }
      }


      points.Add(point);
    }



    private static double Dot(
      Vector3d a,
      Vector3d b)
    {
      return
        a.X * b.X +
        a.Y * b.Y +
        a.Z * b.Z;
    }



    private static bool IsFinite(
      double x)
    {
      return
        !double.IsNaN(x) &&
        !double.IsInfinity(x);
    }



    private static string Num(
      double x)
    {
      if (double.IsNaN(x))
        return "N/A";

      if (double.IsPositiveInfinity(x))
        return "+Infinity";

      if (double.IsNegativeInfinity(x))
        return "-Infinity";


      return x.ToString("G12");
    }
  }
}
