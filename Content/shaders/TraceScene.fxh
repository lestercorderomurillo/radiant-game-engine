Texture2D SceneData : register(t0);
SamplerState DataSampler : register(s0);
float2 DataSize;
int DataWidth;
int TriangleOffset;
int NodeCount;

float4 Data(int index) { return SceneData.SampleLevel(DataSampler, (float2(index % DataWidth, index / DataWidth) + 0.5) / DataSize, 0); }

bool Bounds(float3 origin, float3 direction, float3 low, float3 high, float closest)
{
    float tMin = 0.0001, tMax = closest;
    [unroll] for (int axis = 0; axis < 3; axis++)
    {
        if (abs(direction[axis]) < 1e-8) { if (origin[axis] < low[axis] || origin[axis] > high[axis]) return false; }
        else
        {
            float a = (low[axis] - origin[axis]) / direction[axis], b = (high[axis] - origin[axis]) / direction[axis];
            tMin = max(tMin, min(a, b)); tMax = min(tMax, max(a, b));

            if (tMax < tMin) return false;
        }
    }

    return true;
}

bool Intersect(float3 origin, float3 direction, float maxDistance, out float distance, out float3 normal, out float3 albedo, out float3 emission)
{
    distance = maxDistance; normal = albedo = emission = 0;
    bool hit = false;
    int node = 0;
    [loop] for (int traversal = 0; traversal < NodeCount && node < NodeCount; traversal++)
    {
        float4 low = Data(node * 3), high = Data(node * 3 + 1);
        int count = (int)Data(node * 3 + 2).x;

        if (!Bounds(origin, direction, low.xyz, high.xyz, distance)) { node = max(node + 1, (int)high.w); continue; }

        [loop] for (int i = 0; i < count; i++)
        {
            int k = TriangleOffset + ((int)low.w + i) * 5;
            float3 a = Data(k).xyz, e1 = Data(k + 1).xyz, e2 = Data(k + 2).xyz;
            float3 p = cross(direction, e2);
            float determinant = dot(e1, p);

            if (abs(determinant) < 1e-8) continue;

            float3 t = origin - a;
            float u = dot(t, p) / determinant;

            if (u < 0 || u > 1) continue;

            float3 q = cross(t, e1);
            float v = dot(direction, q) / determinant;
            float d = dot(e2, q) / determinant;

            if (v < 0 || u + v > 1 || d <= 0.0001 || d >= distance) continue;

            hit = true; distance = d;
            normal = normalize(cross(e1, e2));
            if (dot(normal, direction) > 0) normal = -normal;
            albedo = Data(k + 3).xyz; emission = Data(k + 4).xyz;
        }

        node++;
    }

    return hit;
}
