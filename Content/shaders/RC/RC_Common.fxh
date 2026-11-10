/* RC_Common.fxh
   Shared addressing for world-space radiance cascades: probe grids, equal-area directions,
   atlas tiles and the per-probe irradiance lookup used by shading and bounce feedback. */

static const float RC_PI = 3.14159265359;
static const float RC_TWO_PI = 6.28318530718;

// Equal-area sphere mapping around the world Y axis. u wraps; v = 0 and v = 1 are the poles.
float3 DecodeDirection(float2 uv)
{
    float phi = uv.x * RC_TWO_PI;
    float y = 2.0 * uv.y - 1.0;
    float radius = sqrt(max(0.0, 1.0 - y * y));
    return float3(radius * cos(phi), y, radius * sin(phi));
}

float2 EncodeDirection(float3 direction)
{
    float phi = atan2(direction.z, direction.x);
    return float2(frac(phi / RC_TWO_PI), saturate((direction.y + 1.0) * 0.5));
}

// Odd row counts include the equator; columns align with X/Z so all six axis normals are exact.
float3 IrradianceNormal(int2 texel, int2 tileSize)
{
    int columns = tileSize.x - 2;
    int column = (texel.x + columns - 1) % columns;
    float phi = (float)column / columns * RC_TWO_PI;
    float y = 2.0 * texel.y / max(tileSize.y - 1, 1) - 1.0;
    float radius = sqrt(max(0.0, 1.0 - y * y));
    return float3(radius * cos(phi), y, radius * sin(phi));
}

int ProbeIndex(int3 cell, int gridSize) { return cell.x + gridSize * (cell.y + gridSize * cell.z); }

int3 ProbeCell(int probeIndex, int gridSize) { return int3(probeIndex % gridSize, (probeIndex / gridSize) % gridSize, probeIndex / (gridSize * gridSize)); }

float3 ProbePosition(int3 cell, float3 gridMin, float spacing) { return gridMin + (float3(cell) + 0.5) * spacing; }

int2 TileOrigin(int probeIndex, int tilesPerRow, int2 tileSize) { return int2(probeIndex % tilesPerRow, probeIndex / tilesPerRow) * tileSize; }

// Visibility needs only the first blocker, without material reads or a closest-hit search.
bool ProbeOccluded(float3 origin, float3 direction, float maxDistance)
{
    int node = 0;
    [loop] for (int traversal = 0; traversal < NodeCount && node < NodeCount; traversal++)
    {
        float4 low = Data(node * 3), high = Data(node * 3 + 1);
        if (!Bounds(origin, direction, low.xyz, high.xyz, maxDistance)) { node = max(node + 1, (int)high.w); continue; }

        int count = (int)Data(node * 3 + 2).x;
        [loop] for (int triangleIndex = 0; triangleIndex < count; triangleIndex++)
        {
            int address = TriangleOffset + ((int)low.w + triangleIndex) * 5;
            float3 vertex = Data(address).xyz;
            float3 edge1 = Data(address + 1).xyz, edge2 = Data(address + 2).xyz;
            float3 perpendicular = cross(direction, edge2);
            float determinant = dot(edge1, perpendicular);
            if (abs(determinant) < 1e-8) continue;

            float3 offset = origin - vertex;
            float barycentricU = dot(offset, perpendicular) / determinant;
            if (barycentricU < 0 || barycentricU > 1) continue;

            float3 crossOffset = cross(offset, edge1);
            float barycentricV = dot(direction, crossOffset) / determinant;
            float distance = dot(edge2, crossOffset) / determinant;
            if (barycentricV >= 0 && barycentricU + barycentricV <= 1 && distance > 0.0001 && distance < maxDistance) return true;
        }
        node++;
    }
    return false;
}

// Wrapped border columns let the hardware filter the normal tile with a single fetch.
float3 ProbeIrradiance(Texture2D volume, SamplerState linearSampler, float2 atlasSize, int2 origin, int2 tileSize, float3 normal)
{
    float2 uv = EncodeDirection(normal);
    float2 texel = float2(uv.x * (tileSize.x - 2) + 1.5, uv.y * (tileSize.y - 1) + 0.5);
    return volume.SampleLevel(linearSampler, (origin + texel) / atlasSize, 0).rgb;
}

// Trilinear probe interpolation with a normal bias; probes behind the surface or hidden by geometry lose their weight.
// Requires TraceScene.fxh when visibility is requested.
float3 SampleIrradiance(Texture2D volume, SamplerState linearSampler, float2 atlasSize, int tilesPerRow, int2 tileSize,
                        int gridSize, float3 gridMin, float spacing, float3 position, float3 normal, float normalBias, bool visibility)
{
    float3 biased = position + normal * normalBias * spacing;
    float3 grid = (biased - gridMin) / spacing - 0.5;
    float3 base = floor(grid);
    float3 fraction = saturate(grid - base);
    float3 total = 0;
    float weightSum = 0;
    [loop] for (int corner = 0; corner < 8; corner++)
    {
        int3 offset = int3(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1);
        int3 cell = clamp(int3(base) + offset, 0, gridSize - 1);
        float3 trilinear = lerp(1.0 - fraction, fraction, float3(offset));
        float3 probe = ProbePosition(cell, gridMin, spacing);
        float facing = saturate(dot(normalize(probe - position + normal * (0.001 * spacing)), normal) * 1.5 + 0.5);
        float weight = trilinear.x * trilinear.y * trilinear.z * (facing + 0.001);

        if (visibility && weight > 1e-5)
        {
            // Bias selects interpolation probes, but must not move visibility past a nearby blocker.
            float3 visibilityOrigin = position + normal * (0.001 * spacing);
            float3 toProbe = probe - visibilityOrigin;
            float probeDistance = max(length(toProbe), 1e-5);
            if (ProbeOccluded(visibilityOrigin, toProbe / probeDistance, probeDistance)) weight *= 0.001;
        }

        total += weight * ProbeIrradiance(volume, linearSampler, atlasSize, TileOrigin(ProbeIndex(cell, gridSize), tilesPerRow, tileSize), tileSize, normal);
        weightSum += weight;
    }

    return total / max(weightSum, 1e-6);
}
