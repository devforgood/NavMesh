﻿using SharpNav;
using SharpNav.Geometry;
using SharpNav.IO.Json;
using SharpNav.Pathfinding;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using SVector3 = SharpNav.Geometry.Vector3;
using Debug = UnityEngine.Debug;
using Vector3 = UnityEngine.Vector3;

public class testGame : MonoBehaviour
{
    //Generate poly mesh
    private Heightfield heightfield;
    private CompactHeightfield compactHeightfield;
    private ContourSet contourSet;
    private PolyMesh polyMesh;
    private PolyMeshDetail polyMeshDetail;


    private NavMeshBuilder buildData;
    private TiledNavMesh tiledNavMesh;
    private NavMeshQuery navMeshQuery;

    //Smooth path for a single unit
    private NavPoint startPt;
    private NavPoint endPt;
    private Path path;
    private List<SharpNav.Geometry.Vector3> smoothPath;

    private bool hasGenerated;
    private bool interceptExceptions;



    private ObjModel level;

    private NavMeshGenerationSettings settings = NavMeshGenerationSettings.Default;


    // 길찾기 시뮬레이터
    int CurrentNode = 0;
    Vector3 TargetPosition;

    // Start is called before the first frame update
    void Start()
    {
        level = new ObjModel(@"ExportedObj;GameDA_3096.obj");

        settings.AgentRadius = 0.2f;
        settings.AgentHeight = 1.0f;

        GenerateNavMesh();

        TargetPosition = ExportNavMeshToObj.ToUnityVector(smoothPath[CurrentNode]);
        transform.position = TargetPosition;
    }

    private void GenerateNavMesh()
    {
        Debug.Log("Generating NavMesh");

        Stopwatch sw = new Stopwatch();
        sw.Start();
        long prevMs = 0;
        try
        {
            //level.SetBoundingBoxOffset(new SVector3(settings.CellSize * 0.5f, settings.CellHeight * 0.5f, settings.CellSize * 0.5f));
            var levelTris = level.GetTriangles();
            var triEnumerable = TriangleEnumerable.FromTriangle(levelTris, 0, levelTris.Length);
            BBox3 bounds = triEnumerable.GetBoundingBox();

            heightfield = new Heightfield(bounds, settings);

            Debug.Log("Heightfield");
            Debug.Log(" + Ctor\t\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
            prevMs = sw.ElapsedMilliseconds;

            /*Area[] areas = AreaGenerator.From(triEnumerable, Area.Default)
                .MarkAboveHeight(areaSettings.MaxLevelHeight, Area.Null)
                .MarkBelowHeight(areaSettings.MinLevelHeight, Area.Null)
                .MarkBelowSlope(areaSettings.MaxTriSlope, Area.Null)
                .ToArray();
            heightfield.RasterizeTrianglesWithAreas(levelTris, areas);*/
            heightfield.RasterizeTriangles(levelTris, Area.Default);

            Debug.Log(" + Rasterization\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
            Debug.Log(" + Filtering");
            prevMs = sw.ElapsedMilliseconds;

            heightfield.FilterLedgeSpans(settings.VoxelAgentHeight, settings.VoxelMaxClimb);

            Debug.Log("   + Ledge Spans\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
            prevMs = sw.ElapsedMilliseconds;

            heightfield.FilterLowHangingWalkableObstacles(settings.VoxelMaxClimb);

            Debug.Log("   + Low Hanging Obstacles\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
            prevMs = sw.ElapsedMilliseconds;

            heightfield.FilterWalkableLowHeightSpans(settings.VoxelAgentHeight);

            Debug.Log("   + Low Height Spans\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
            prevMs = sw.ElapsedMilliseconds;

            compactHeightfield = new CompactHeightfield(heightfield, settings);

            Debug.Log("CompactHeightfield");
            Debug.Log(" + Ctor\t\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
            prevMs = sw.ElapsedMilliseconds;

            compactHeightfield.Erode(settings.VoxelAgentRadius);

            Debug.Log(" + Erosion\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
            prevMs = sw.ElapsedMilliseconds;

            compactHeightfield.BuildDistanceField();

            Debug.Log(" + Distance Field\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
            prevMs = sw.ElapsedMilliseconds;

            compactHeightfield.BuildRegions(0, settings.MinRegionSize, settings.MergedRegionSize);

            Debug.Log(" + Regions\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
            prevMs = sw.ElapsedMilliseconds;


            contourSet = compactHeightfield.BuildContourSet(settings);

            Debug.Log("ContourSet");
            Debug.Log(" + Ctor\t\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
            prevMs = sw.ElapsedMilliseconds;

            polyMesh = new PolyMesh(contourSet, settings);

            Debug.Log("PolyMesh");
            Debug.Log(" + Ctor\t\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
            prevMs = sw.ElapsedMilliseconds;

            polyMeshDetail = new PolyMeshDetail(polyMesh, compactHeightfield, settings);

            Debug.Log("PolyMeshDetail");
            Debug.Log(" + Ctor\t\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
            prevMs = sw.ElapsedMilliseconds;

            hasGenerated = true;


        }
        catch (Exception e)
        {
            if (!interceptExceptions)
                throw;
            else
                Debug.Log("Navmesh generation failed with exception:" + Environment.NewLine + e.ToString());
        }
        finally
        {
            sw.Stop();
        }

        if (hasGenerated)
        {
            try
            {
                GeneratePathfinding();

                //Pathfinding with multiple units
                //GenerateCrowd();
            }
            catch (Exception e)
            {
                Debug.Log("Pathfinding generation failed with exception" + Environment.NewLine + e.ToString());
                hasGenerated = false;
            }

            //Label l = (Label)statusBar.FindChildByName("GenTime");
            //l.Text = "Generation Time: " + sw.ElapsedMilliseconds + "ms";

            Debug.Log("Navmesh generated successfully in " + sw.ElapsedMilliseconds + "ms.");
            Debug.Log("Rasterized " + level.GetTriangles().Length + " triangles.");
            Debug.Log("Generated " + contourSet.Count + " regions.");
            Debug.Log("PolyMesh contains " + polyMesh.VertCount + " vertices in " + polyMesh.PolyCount + " polys.");
            Debug.Log("PolyMeshDetail contains " + polyMeshDetail.VertCount + " vertices and " + polyMeshDetail.TrisCount + " tris in " + polyMeshDetail.MeshCount + " meshes.");
        }
    }

    private void GeneratePathfinding()
    {
        if (!hasGenerated)
            return;

        NavQueryFilter filter = new NavQueryFilter();

        buildData = new NavMeshBuilder(polyMesh, polyMeshDetail, new SharpNav.Pathfinding.OffMeshConnection[0], settings);

        tiledNavMesh = new TiledNavMesh(buildData);

        for (int i = 0; i < tiledNavMesh.Tiles.Count; ++i)
        {
            for (int j = 0; j < tiledNavMesh.Tiles[i].Verts.Length; ++j)
            {
                if (j < tiledNavMesh.Tiles[i].Verts.Length - 1)
                    Debug.DrawLine(ExportNavMeshToObj.ToUnityVector(tiledNavMesh.Tiles[i].Verts[j]), ExportNavMeshToObj.ToUnityVector(tiledNavMesh.Tiles[i].Verts[j + 1]), Color.blue, 99);
            }
        }

        navMeshQuery = new NavMeshQuery(tiledNavMesh, 2048);

        //Find random start and end points on the poly mesh
        /*int startRef;
        navMeshQuery.FindRandomPoint(out startRef, out startPos);*/

        //SVector3 c = new SVector3(10, 0, 0);
        //SVector3 e = new SVector3(5, 5, 5);
        //navMeshQuery.FindNearestPoly(ref c, ref e, out startPt);

        //navMeshQuery.FindRandomPointAroundCircle(ref startPt, 1000, out endPt);

        startPt = navMeshQuery.FindRandomPoint();
        endPt = navMeshQuery.FindRandomPoint();

        //calculate the overall path, which contains an array of polygon references
        int MAX_POLYS = 256;
        path = new Path();
        navMeshQuery.FindPath(ref startPt, ref endPt, filter, path);

        //find a smooth path over the mesh surface
        int npolys = path.Count;
        SVector3 iterPos = new SVector3();
        SVector3 targetPos = new SVector3();
        navMeshQuery.ClosestPointOnPoly(startPt.Polygon, startPt.Position, ref iterPos);
        navMeshQuery.ClosestPointOnPoly(path[npolys - 1], endPt.Position, ref targetPos);

        smoothPath = new List<SVector3>(2048);
        smoothPath.Add(iterPos);

        float STEP_SIZE = 0.5f;
        float SLOP = 0.01f;
        while (npolys > 0 && smoothPath.Count < smoothPath.Capacity)
        {
            //find location to steer towards
            SVector3 steerPos = new SVector3();
            StraightPathFlags steerPosFlag = 0;
            NavPolyId steerPosRef = NavPolyId.Null;

            if (!GetSteerTarget(navMeshQuery, iterPos, targetPos, SLOP, path, ref steerPos, ref steerPosFlag, ref steerPosRef))
                break;

            bool endOfPath = (steerPosFlag & StraightPathFlags.End) != 0 ? true : false;
            bool offMeshConnection = (steerPosFlag & StraightPathFlags.OffMeshConnection) != 0 ? true : false;

            //find movement delta
            SVector3 delta = steerPos - iterPos;
            float len = (float)Math.Sqrt(SVector3.Dot(delta, delta));

            //if steer target is at end of path or off-mesh link
            //don't move past location
            if ((endOfPath || offMeshConnection) && len < STEP_SIZE)
                len = 1;
            else
                len = STEP_SIZE / len;

            SVector3 moveTgt = new SVector3();
            VMad(ref moveTgt, iterPos, delta, len);

            //move
            SVector3 result = new SVector3();
            List<NavPolyId> visited = new List<NavPolyId>(16);
            NavPoint startPoint = new NavPoint(path[0], iterPos);
            navMeshQuery.MoveAlongSurface(ref startPoint, ref moveTgt, out result, visited);
            path.FixupCorridor(visited);
            npolys = path.Count;
            float h = 0;
            navMeshQuery.GetPolyHeight(path[0], result, ref h);
            result.Y = h;
            iterPos = result;

            //handle end of path when close enough
            if (endOfPath && InRange(iterPos, steerPos, SLOP, 1.0f))
            {
                //reached end of path
                iterPos = targetPos;
                if (smoothPath.Count < smoothPath.Capacity)
                {
                    smoothPath.Add(iterPos);
                }
                break;
            }

            //store results
            if (smoothPath.Count < smoothPath.Capacity)
            {
                smoothPath.Add(iterPos);
            }
        }

        for (int i = 0; i < smoothPath.Count; i++)
        {
            if(i< smoothPath.Count - 1)
                Debug.DrawLine(ExportNavMeshToObj.ToUnityVector(smoothPath[i]), ExportNavMeshToObj.ToUnityVector(smoothPath[i+1]), Color.red, 99);

        }
    }


    private bool GetSteerTarget(NavMeshQuery navMeshQuery, SVector3 startPos, SVector3 endPos, float minTargetDist, SharpNav.Pathfinding.Path path,
    ref SVector3 steerPos, ref StraightPathFlags steerPosFlag, ref NavPolyId steerPosRef)
    {
        StraightPath steerPath = new StraightPath();
        navMeshQuery.FindStraightPath(startPos, endPos, path, steerPath, 0);
        int nsteerPath = steerPath.Count;
        if (nsteerPath == 0)
            return false;

        //find vertex far enough to steer to
        int ns = 0;
        while (ns < nsteerPath)
        {
            if ((steerPath[ns].Flags & StraightPathFlags.OffMeshConnection) != 0 ||
                !InRange(steerPath[ns].Point.Position, startPos, minTargetDist, 1000.0f))
                break;

            ns++;
        }

        //failed to find good point to steer to
        if (ns >= nsteerPath)
            return false;

        steerPos = steerPath[ns].Point.Position;
        steerPos.Y = startPos.Y;
        steerPosFlag = steerPath[ns].Flags;
        if (steerPosFlag == StraightPathFlags.None && ns == (nsteerPath - 1))
            steerPosFlag = StraightPathFlags.End; // otherwise seeks path infinitely!!!
        steerPosRef = steerPath[ns].Point.Polygon;

        return true;
    }

    private bool InRange(SVector3 v1, SVector3 v2, float r, float h)
    {
        float dx = v2.X - v1.X;
        float dy = v2.Y - v1.Y;
        float dz = v2.Z - v1.Z;
        return (dx * dx + dz * dz) < (r * r) && Math.Abs(dy) < h;
    }

    /// <summary>
    /// Scaled vector addition
    /// </summary>
    /// <param name="dest">Result</param>
    /// <param name="v1">Vector 1</param>
    /// <param name="v2">Vector 2</param>
    /// <param name="s">Scalar</param>
    private void VMad(ref SVector3 dest, SVector3 v1, SVector3 v2, float s)
    {
        dest.X = v1.X + v2.X * s;
        dest.Y = v1.Y + v2.Y * s;
        dest.Z = v1.Z + v2.Z * s;
    }



    // Update is called once per frame
    void Update()
    {
        if(transform.position != TargetPosition)
        {
            transform.position = Vector3.Lerp(transform.position, TargetPosition, Time.deltaTime * 50.0f);
        }
        else
        {
            if (CurrentNode<smoothPath.Count-1)
            {
                ++CurrentNode;
                TargetPosition = ExportNavMeshToObj.ToUnityVector(smoothPath[CurrentNode]);
            }
        }
    }
}
