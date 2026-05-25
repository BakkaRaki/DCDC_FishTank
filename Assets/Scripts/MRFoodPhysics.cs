using Meta.XR.MRUtilityKit;

using UnityEngine;



/// <summary>

/// Food landing: MRUK standable anchors + FLOOR/TABLE EffectMesh (never GLOBAL_MESH as floor).

/// </summary>

public static class MRFoodPhysics

{

    public const float DefaultSurfaceSkin = 0.025f;

    public const float StandableSurfaceLift = 0.02f;

    public const float DefaultGravityScale = 0.35f;



    const float MaxFallSpeed = 2.5f;

    const float DefaultMaxDrop = 8f;

    const float TableFootprintMargin = 0.75f;
    const float FoodProbeSphereRadius = 0.06f;

    const float MaxSurfaceAboveCenter = 0.12f;

    const float MaxPenetrationRecover = 1.5f;



    public static bool HasRoomCache { get; private set; }

    public static float CachedFloorY { get; private set; }

    public static Vector3 CachedFloorPoint { get; private set; }



    public static float GetRestOffset(float skin) => skin + StandableSurfaceLift;



    public static void InvalidateRoomCache() => HasRoomCache = false;



    public static void EnsureFoodCollidesWithRealWorld()

    {

        int food = LayerMask.NameToLayer("Food");

        int rw = LayerMask.NameToLayer("RealWorld");

        if (food < 0 || rw < 0) return;

        Physics.IgnoreLayerCollision(food, rw, false);

    }



    public static MRUKRoom ResolveRoom()

    {

        if (MRUK.Instance == null) return null;

        MRUKRoom room = MRUK.Instance.GetCurrentRoom();

        return room != null ? room : MRUKBootstrap.LastLoadedRoom;

    }



    public static void PrepareForFoodSpawn()

    {

        InvalidateRoomCache();

        MRUKRoom room = ResolveRoom();

        if (room != null)

            UpdateRoomCache(room);

    }



    public static void EnsureRoomCache()

    {

        if (HasRoomCache) return;

        MRUKRoom room = ResolveRoom();

        if (room != null)

            UpdateRoomCache(room);

    }



    static float GetCameraY() => Camera.main != null ? Camera.main.transform.position.y : 0f;



    static void GetFloorHeightBand(out float minY, out float maxY)

    {

        float camY = GetCameraY();

        minY = camY - 2.5f;

        maxY = camY + 0.15f;

    }



    static bool IsPlausibleFloorHeight(float surfaceY)

    {

        GetFloorHeightBand(out float minY, out float maxY);

        return surfaceY >= minY && surfaceY <= maxY;

    }



    static bool IsPlausibleFurnitureHeight(float surfaceY)

    {

        GetFloorHeightBand(out float minY, out _);

        return surfaceY >= minY && surfaceY <= GetCameraY() + 1.6f;

    }



    static bool IsExcludedColliderName(string name)

    {

        if (string.IsNullOrEmpty(name)) return true;

        if (name.IndexOf("GLOBAL_MESH", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;

        if (name.IndexOf("CEILING", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;

        if (name.IndexOf("INVISIBLE_WALL", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;

        if (name.IndexOf("WALL", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;

        return false;

    }



    static bool IsFloorEffectMeshCollider(Collider col)

    {

        if (col == null) return false;

        string name = col.name;

        if (IsExcludedColliderName(name)) return false;

        return name.IndexOf("FLOOR", System.StringComparison.OrdinalIgnoreCase) >= 0 &&

               name.IndexOf("EffectMesh", System.StringComparison.OrdinalIgnoreCase) >= 0;

    }



    static bool IsTableEffectMeshCollider(Collider col)

    {

        if (col == null) return false;

        string name = col.name;

        if (IsExcludedColliderName(name)) return false;

        return name.IndexOf("TABLE", System.StringComparison.OrdinalIgnoreCase) >= 0 &&

               name.IndexOf("EffectMesh", System.StringComparison.OrdinalIgnoreCase) >= 0;

    }



    static bool IsFurnitureEffectMeshCollider(Collider col)

    {

        if (col == null) return false;

        if (IsTableEffectMeshCollider(col)) return true;

        string name = col.name;

        if (IsExcludedColliderName(name)) return false;

        return name.IndexOf("EffectMesh", System.StringComparison.OrdinalIgnoreCase) >= 0 &&

               (name.IndexOf("COUCH", System.StringComparison.OrdinalIgnoreCase) >= 0 ||

                name.IndexOf("STORAGE", System.StringComparison.OrdinalIgnoreCase) >= 0);

    }



    static bool HitsFurnitureEffectMeshBelow(Vector3 pos, float maxDist)

    {

        Vector3 origin = pos + Vector3.up * 0.12f;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDist, GetSupportCastMask(),

                QueryTriggerInteraction.Ignore) && IsFurnitureEffectMeshCollider(hit.collider))

            return true;



        RaycastHit[] hits = Physics.SphereCastAll(origin, FoodProbeSphereRadius, Vector3.down, maxDist,

            GetSupportCastMask(), QueryTriggerInteraction.Ignore);

        foreach (var h in hits)

        {

            if (h.collider != null && h.normal.y >= 0.35f && IsFurnitureEffectMeshCollider(h.collider))

                return true;

        }



        return false;

    }



    static bool IsStandablePhysicsCollider(Collider col)

    {

        if (col == null) return false;

        if (IsFloorEffectMeshCollider(col) || IsTableEffectMeshCollider(col)) return true;

        string name = col.name;

        if (IsExcludedColliderName(name)) return false;

        if (name.IndexOf("PhysicsProxy_TABLE", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;

        if (name.IndexOf("PhysicsProxy_COUCH", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;

        if (name.IndexOf("PhysicsProxy_STORAGE", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;

        return false;

    }



    static bool TryGetFloorYFromAnchors(MRUKRoom room, Vector3 nearXZ, out float floorY, out Vector3 floorPoint)

    {

        floorY = float.NegativeInfinity;

        floorPoint = nearXZ;

        bool found = false;

        if (room?.Anchors == null) return false;



        foreach (var anchor in room.Anchors)

        {

            if (!IsAnchorUsable(anchor)) continue;

            if ((anchor.Label & MRUKAnchor.SceneLabels.FLOOR) == 0) continue;



            float y = GetStandableSurfaceY(anchor, nearXZ);

            if (!IsPlausibleFloorHeight(y)) continue;

            if (!found || y > floorY)

            {

                floorY = y;

                floorPoint = new Vector3(nearXZ.x, y, nearXZ.z);

                found = true;

            }

        }



        return found;

    }



    public static void UpdateRoomCache(MRUKRoom room)

    {

        HasRoomCache = false;

        CachedFloorY = 0f;

        CachedFloorPoint = Vector3.zero;

        if (room == null) return;



        Vector3 probeXZ = Camera.main != null ? Camera.main.transform.position : Vector3.zero;



        if (Camera.main != null)

        {

            RaycastHit[] hits = Physics.RaycastAll(probeXZ + Vector3.up * 0.25f, Vector3.down, 3f,

                GetSupportCastMask(), QueryTriggerInteraction.Ignore);

            float bestTop = float.NegativeInfinity;

            foreach (var hit in hits)

            {

                if (hit.collider == null || hit.normal.y < 0.5f) continue;

                if (!IsFloorEffectMeshCollider(hit.collider)) continue;



                float topY = hit.collider.bounds.max.y;

                if (!IsPlausibleFloorHeight(topY) || topY <= bestTop) continue;



                bestTop = topY;

                CachedFloorPoint = new Vector3(probeXZ.x, topY + GetRestOffset(DefaultSurfaceSkin), probeXZ.z);

                CachedFloorY = CachedFloorPoint.y;

                HasRoomCache = true;

            }



            if (HasRoomCache)

            {

                FishTankLog.Warn($"Room cache floorY={CachedFloorY:F2} (FLOOR EffectMesh) point={CachedFloorPoint}");

                return;

            }

        }



        if (TryGetFloorYFromAnchors(room, probeXZ, out float anchorFloorY, out Vector3 anchorPt))

        {

            CachedFloorPoint = anchorPt + Vector3.up * GetRestOffset(DefaultSurfaceSkin);

            CachedFloorY = CachedFloorPoint.y;

            HasRoomCache = true;

            FishTankLog.Warn($"Room cache floorY={CachedFloorY:F2} (FLOOR anchor) point={CachedFloorPoint}");

            return;

        }



        if (TryMrukLabeledSurface(probeXZ, MRUKAnchor.SceneLabels.FLOOR, out Vector3 mrukFloor, out _))

        {

            CachedFloorPoint = mrukFloor + Vector3.up * GetRestOffset(DefaultSurfaceSkin);

            CachedFloorY = CachedFloorPoint.y;

            HasRoomCache = true;

            FishTankLog.Warn($"Room cache floorY={CachedFloorY:F2} (MRUK FLOOR) point={CachedFloorPoint}");

        }

    }



    static int GetSupportCastMask()

    {

        int rw = LayerMask.NameToLayer("RealWorld");

        return rw >= 0 ? (1 << rw) | Physics.DefaultRaycastLayers : Physics.DefaultRaycastLayers;

    }



    static bool IsSupportLabel(MRUKAnchor.SceneLabels label)

    {

        return (label & (MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.TABLE |

                         MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.STORAGE)) != 0;

    }



    static bool RequiresFootprint(MRUKAnchor.SceneLabels label) =>

        (label & (MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH |

                  MRUKAnchor.SceneLabels.STORAGE)) != 0;



    static bool IsAnchorUsable(MRUKAnchor anchor) => anchor != null && anchor.transform != null;



    public static Vector3 GetStandableNormal(Transform t)

    {

        Vector3 best = t.forward;

        float bestDot = Vector3.Dot(best.normalized, Vector3.up);



        void Try(Vector3 v)

        {

            float d = Vector3.Dot(v.normalized, Vector3.up);

            if (d > bestDot)

            {

                bestDot = d;

                best = v;

            }

        }



        Try(t.forward);

        Try(-t.forward);

        Try(t.up);

        Try(-t.up);

        Try(t.right);

        Try(-t.right);



        if (Vector3.Dot(best, Vector3.up) < 0f)

            best = -best;

        return best.normalized;

    }



    static Vector3 GetStandableNormal(MRUKAnchor anchor) =>

        IsAnchorUsable(anchor) ? GetStandableNormal(anchor.transform) : Vector3.up;



    public static Vector3 GetAnchorStandableSurfacePoint(MRUKAnchor anchor, Vector3 planeNormal)

    {

        if (!anchor.PlaneRect.HasValue)

            return anchor.GetAnchorCenter();



        Rect r = anchor.PlaneRect.Value;

        Transform t = anchor.transform;

        float bestAlong = float.NegativeInfinity;

        Vector3 best = anchor.GetAnchorCenter();



        void TryCorner(float x, float y)

        {

            Vector3 world = t.TransformPoint(new Vector3(x, y, 0f));

            float along = Vector3.Dot(world, planeNormal);

            if (along > bestAlong)

            {

                bestAlong = along;

                best = world;

            }

        }



        TryCorner(r.xMin, r.yMin);

        TryCorner(r.xMax, r.yMin);

        TryCorner(r.xMin, r.yMax);

        TryCorner(r.xMax, r.yMax);

        return best;

    }



    static Vector3 ProjectOnPlane(Vector3 worldPos, Vector3 planePoint, Vector3 planeNormal)

    {

        float signedHeight = Vector3.Dot(worldPos - planePoint, planeNormal);

        return worldPos - planeNormal * signedHeight;

    }



    static bool TryMrukLabeledSurface(Vector3 worldPos, MRUKAnchor.SceneLabels labelFilter, out Vector3 surfacePoint,

        out Vector3 normal, out MRUKAnchor matchedAnchor)

    {

        surfacePoint = worldPos;

        normal = Vector3.up;

        matchedAnchor = null;



        MRUKRoom room = ResolveRoom();

        if (room == null) return false;



        Vector3 probe = worldPos + Vector3.up * 0.2f;

        float dist = room.TryGetClosestSurfacePosition(probe, out Vector3 surf, out MRUKAnchor anchor, out normal);

        if (dist < 0f || !IsAnchorUsable(anchor)) return false;

        if ((anchor.Label & labelFilter) == 0) return false;

        if (normal.y < 0.35f) return false;



        matchedAnchor = anchor;

        surfacePoint = new Vector3(worldPos.x, surf.y, worldPos.z);

        return true;

    }



    static bool TryMrukLabeledSurface(Vector3 worldPos, MRUKAnchor.SceneLabels labelFilter, out Vector3 surfacePoint,

        out Vector3 normal)

    {

        return TryMrukLabeledSurface(worldPos, labelFilter, out surfacePoint, out normal, out _);

    }



    static float GetStandableSurfaceY(MRUKAnchor anchor, Vector3 worldPos)

    {

        Vector3 n = GetStandableNormal(anchor);



        if (RequiresFootprint(anchor.Label) &&

            TryMrukLabeledSurface(worldPos, anchor.Label & (MRUKAnchor.SceneLabels.TABLE |

                                                             MRUKAnchor.SceneLabels.COUCH |

                                                             MRUKAnchor.SceneLabels.STORAGE),

                out Vector3 mrukPt, out _))

            return mrukPt.y;



        return GetAnchorStandableSurfacePoint(anchor, n).y;

    }



    static bool IsOverAnchorFootprint(MRUKAnchor anchor, Vector3 worldPos, Vector3 planeNormal, float margin)

    {

        if (!anchor.PlaneRect.HasValue) return true;



        Vector3 surfaceRef = GetAnchorStandableSurfacePoint(anchor, planeNormal);

        Vector3 onPlane = ProjectOnPlane(worldPos, surfaceRef, planeNormal);

        Vector3 local = anchor.transform.InverseTransformPoint(onPlane);

        Rect r = anchor.PlaneRect.Value;

        return local.x >= r.xMin - margin && local.x <= r.xMax + margin &&

               local.y >= r.yMin - margin && local.y <= r.yMax + margin;

    }



    static bool IsValidGap(float gap, float maxDrop) =>

        gap <= maxDrop + 0.15f && gap >= -MaxPenetrationRecover;



    static void ConsiderSurface(Vector3 pos, float maxDrop, float surfaceY, Vector3 supportPoint, Vector3 normal,

        MRUKAnchor.SceneLabels label, ref bool found, ref float bestY, ref Vector3 bestPoint, ref Vector3 bestNormal)

    {

        if (normal.y < 0.2f) return;



        bool isFloor = (label & MRUKAnchor.SceneLabels.FLOOR) != 0;

        if (isFloor && !IsPlausibleFloorHeight(surfaceY)) return;

        if (!isFloor && !IsPlausibleFurnitureHeight(surfaceY)) return;



        float gap = pos.y - surfaceY;

        if (!IsValidGap(gap, maxDrop)) return;

        if (surfaceY > pos.y + MaxSurfaceAboveCenter) return;



        if (!found || surfaceY > bestY)

        {

            bestY = surfaceY;

            bestPoint = supportPoint;

            bestNormal = normal;

            found = true;

        }

    }



    static void CollectFromAnchors(Vector3 pos, float maxDrop, ref bool found, ref float bestY,

        ref Vector3 bestPoint, ref Vector3 bestNormal)

    {

        MRUKRoom room = ResolveRoom();

        if (room?.Anchors == null) return;



        foreach (var anchor in room.Anchors)

        {

            if (!IsAnchorUsable(anchor) || !anchor.PlaneRect.HasValue || !IsSupportLabel(anchor.Label)) continue;



            Vector3 n = GetStandableNormal(anchor);

            if (n.y < 0.25f) continue;



            if (RequiresFootprint(anchor.Label) && !IsOverAnchorFootprint(anchor, pos, n, TableFootprintMargin))

            {

                MRUKAnchor.SceneLabels furnitureMask = MRUKAnchor.SceneLabels.TABLE |

                                                       MRUKAnchor.SceneLabels.COUCH |

                                                       MRUKAnchor.SceneLabels.STORAGE;

                bool mrukOk = TryMrukLabeledSurface(pos, anchor.Label & furnitureMask, out _, out _, out _);

                if (!mrukOk && !HitsFurnitureEffectMeshBelow(pos, 0.65f))

                    continue;

            }



            float surfaceY = GetStandableSurfaceY(anchor, pos);

            Vector3 supportPt = n.y >= 0.75f

                ? new Vector3(pos.x, surfaceY, pos.z)

                : ProjectOnPlane(pos, GetAnchorStandableSurfacePoint(anchor, n), n);



            ConsiderSurface(pos, maxDrop, surfaceY, supportPt, n, anchor.Label, ref found, ref bestY, ref bestPoint,

                ref bestNormal);

        }

    }



    static void CollectFromPhysics(Vector3 pos, float maxDrop, ref bool found, ref float bestY,

        ref Vector3 bestPoint, ref Vector3 bestNormal)

    {

        Vector3 castOrigin = pos + Vector3.up * 0.12f;

        float castDist = maxDrop + 1f;

        RaycastHit[] hits = Physics.SphereCastAll(castOrigin, FoodProbeSphereRadius, Vector3.down, castDist,

            GetSupportCastMask(), QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)

            hits = Physics.RaycastAll(castOrigin, Vector3.down, castDist, GetSupportCastMask(),

                QueryTriggerInteraction.Ignore);

        if (hits == null) return;



        foreach (var hit in hits)

        {

            if (hit.collider == null || hit.normal.y < 0.35f) continue;

            if (!IsStandablePhysicsCollider(hit.collider)) continue;



            float topY = hit.normal.y >= 0.75f

                ? Mathf.Max(hit.point.y, hit.collider.bounds.max.y)

                : hit.point.y;



            MRUKAnchor.SceneLabels label = MRUKAnchor.SceneLabels.FLOOR;

            if (IsTableEffectMeshCollider(hit.collider))

                label = MRUKAnchor.SceneLabels.TABLE;

            else if (hit.collider.name.IndexOf("COUCH", System.StringComparison.OrdinalIgnoreCase) >= 0)

                label = MRUKAnchor.SceneLabels.COUCH;



            Vector3 pt = new Vector3(pos.x, topY, pos.z);

            ConsiderSurface(pos, maxDrop, topY, pt, hit.normal, label, ref found, ref bestY, ref bestPoint,

                ref bestNormal);

        }

    }



    static void CollectFromMrukFurniture(Vector3 pos, float maxDrop, ref bool found, ref float bestY,

        ref Vector3 bestPoint, ref Vector3 bestNormal)

    {

        MRUKAnchor.SceneLabels furniture = MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH |

                                             MRUKAnchor.SceneLabels.STORAGE;

        if (!TryMrukLabeledSurface(pos, furniture, out Vector3 surf, out Vector3 normal, out MRUKAnchor anchor))

            return;



        Vector3 supportPt = new Vector3(pos.x, surf.y, pos.z);

        ConsiderSurface(pos, maxDrop, surf.y, supportPt, normal, anchor.Label, ref found, ref bestY, ref bestPoint,

            ref bestNormal);

    }



    public static bool TryFindSupportBelow(Vector3 pos, float maxDrop, float skin, out Vector3 supportPoint,

        out Vector3 supportNormal)

    {

        supportPoint = pos;

        supportNormal = Vector3.up;

        bool found = false;

        float bestY = float.NegativeInfinity;



        CollectFromPhysics(pos, maxDrop, ref found, ref bestY, ref supportPoint, ref supportNormal);

        CollectFromAnchors(pos, maxDrop, ref found, ref bestY, ref supportPoint, ref supportNormal);

        CollectFromMrukFurniture(pos, maxDrop, ref found, ref bestY, ref supportPoint, ref supportNormal);

        return found;

    }



    public static bool TryFindSupportBelow(Vector3 pos, float maxDrop, out Vector3 supportPoint, out Vector3 supportNormal)

    {

        return TryFindSupportBelow(pos, maxDrop, DefaultSurfaceSkin, out supportPoint, out supportNormal);

    }



    static float RestY(Vector3 support, Vector3 normal, float skin) => support.y + normal.y * GetRestOffset(skin);



    /// <summary>True when position is at or only slightly above the resting height (avoids snapping from pinch height).</summary>

    static bool IsNearRestHeight(Vector3 pos, float restY, float epsilon = 0.05f) => pos.y <= restY + epsilon;



    public static bool TryRecoverStandableSurface(ref Vector3 pos, ref Vector3 velocity, float skin)

    {

        if (TryFindSupportBelow(pos, DefaultMaxDrop, skin, out Vector3 support, out Vector3 normal))

        {

            pos = support + normal * GetRestOffset(skin);

            velocity = Vector3.zero;

            return true;

        }



        EnsureRoomCache();

        if (!HasRoomCache) return false;



        float floorSurfaceY = CachedFloorY - GetRestOffset(skin);

        if (CachedFloorY > pos.y + 0.35f) return false;

        if (pos.y >= floorSurfaceY - 0.08f) return false;



        pos = new Vector3(pos.x, CachedFloorY, pos.z);

        velocity = Vector3.zero;

        FishTankLog.Warn($"Food recover down -> floor y={CachedFloorY:F2} pos={pos}");

        return true;

    }



    public static void SimulateKinematicFall(ref Vector3 position, ref Vector3 velocity, float dt, float gravityScale,

        float skin, int ticksSinceSpawn, int minTicksBeforeRest, float maxDrop = DefaultMaxDrop)

    {

        if (ticksSinceSpawn < 4)

        {

            velocity = Vector3.zero;

            return;

        }



        float restOffset = GetRestOffset(skin);



        if (Mathf.Abs(velocity.y) < 0.01f && ticksSinceSpawn >= minTicksBeforeRest &&

            TryFindSupportBelow(position, maxDrop, skin, out Vector3 restingSupport, out Vector3 restingNormal))

        {

            float restY = RestY(restingSupport, restingNormal, skin);

            if (IsNearRestHeight(position, restY))

            {

                position = restingSupport + restingNormal * restOffset;

                velocity = Vector3.zero;

                return;

            }

        }



        velocity += Physics.gravity * gravityScale * dt;

        if (velocity.y < -MaxFallSpeed)

            velocity.y = -MaxFallSpeed;



        Vector3 motion = velocity * dt;

        float moveLen = motion.magnitude;



        if (moveLen > 1e-5f && velocity.y <= 0f)

        {

            if (Physics.SphereCast(position, FoodProbeSphereRadius, motion.normalized, out RaycastHit hit,

                    moveLen + 0.06f, GetSupportCastMask(), QueryTriggerInteraction.Ignore) &&

                hit.normal.y >= 0.35f && IsStandablePhysicsCollider(hit.collider))

            {

                float topY = hit.normal.y >= 0.75f

                    ? Mathf.Max(hit.point.y, hit.collider.bounds.max.y)

                    : hit.point.y;

                position = new Vector3(position.x, topY, position.z) + hit.normal * restOffset;

                velocity = Vector3.zero;

                return;

            }

        }



        Vector3 next = position + motion;



        if (TryFindSupportBelow(next, maxDrop, skin, out Vector3 support, out Vector3 normal))

        {

            float restY = RestY(support, normal, skin);

            if (velocity.y <= 0f && ticksSinceSpawn >= minTicksBeforeRest &&

                (next.y <= restY + 0.03f || IsNearRestHeight(position, restY)))

            {

                next = support + normal * restOffset;

                velocity = Vector3.zero;

            }

        }

        else if (HasRoomCache && ticksSinceSpawn >= minTicksBeforeRest)

        {

            float floorSurfaceY = CachedFloorY - restOffset;

            if (CachedFloorY <= position.y + 0.35f && next.y < floorSurfaceY - 0.05f)

                TryRecoverStandableSurface(ref next, ref velocity, skin);

        }



        position = next;

    }

}


