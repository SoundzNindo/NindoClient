#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float time;
uniform sampler2D reflectionTexture;
uniform vec2 playerWorldPos;
uniform float playerRippleStrength;
uniform vec2 playerScreenUV;

// Output fragment color
out vec4 finalColor;

// Screen position for world-space noise
in vec2 fragPosition;
in vec2 fragScreenUV;

// 3/4 view wave-foreshortening factor (cos(theta)).
const float COS_THETA = 0.6;

// Distortion strength and optional slow flow drift.
const float DISTORTION_STRENGTH = 0.004;

// Fresnel base reflectance.
const float F0 = 0.03;
const float BASE_WATER_ALPHA = 0.86;

void evaluateAnisotropicWaves(vec2 p, float t, out float h, out vec2 grad)
{
    // p = (u, v), pIso = (u, v * cos(theta))
    vec2 pIso = vec2(p.x, p.y * COS_THETA);

    // Four-wave setup: amplitudes in UV-ish units, frequencies in [6..40], speeds in [0.2..1.2].
    vec2 k1 = vec2(7.0,  4.0);
    vec2 k2 = vec2(-11.0, 6.0);
    vec2 k3 = vec2(15.0, -5.0);
    vec2 k4 = vec2(5.0,  18.0);

    float a1 = 0.010;
    float a2 = 0.007;
    float a3 = 0.0048;
    float a4 = 0.0032;

    float c1 = 0.32;
    float c2 = 0.56;
    float c3 = 0.44;
    float c4 = 0.24;

    float ph1 = 0.0;
    float ph2 = 1.7;
    float ph3 = 3.1;
    float ph4 = 4.2;

    float w1 = c1 * length(k1);
    float w2 = c2 * length(k2);
    float w3 = c3 * length(k3);
    float w4 = c4 * length(k4);

    float s1 = dot(k1, pIso) - w1 * t + ph1;
    float s2 = dot(k2, pIso) - w2 * t + ph2;
    float s3 = dot(k3, pIso) - w3 * t + ph3;
    float s4 = dot(k4, pIso) - w4 * t + ph4;

    float cS1 = cos(s1);
    float cS2 = cos(s2);
    float cS3 = cos(s3);
    float cS4 = cos(s4);

    h = a1 * sin(s1) + a2 * sin(s2) + a3 * sin(s3) + a4 * sin(s4);

    // d h / d u = sum( A_i * k_ix * cos(...) )
    // d h / d v = sum( A_i * k_iy * cos(theta) * cos(...) )
    grad.x = a1 * k1.x * cS1 + a2 * k2.x * cS2 + a3 * k3.x * cS3 + a4 * k4.x * cS4;
    grad.y = (a1 * k1.y * cS1 + a2 * k2.y * cS2 + a3 * k3.y * cS3 + a4 * k4.y * cS4) * COS_THETA;
}

void main()
{
    // Sample the texture
    vec4 texColor = texture(texture0, fragTexCoord);
    
    // CRITICAL: Only discard edge pixels at the actual texture boundaries
    // Check if we're near the texture edges (within 2 pixels)
    float edgeThreshold = 2.0 / 32.0; // 2 pixels for a 32x32 texture
    bool isNearEdge = (fragTexCoord.x < edgeThreshold || fragTexCoord.x > (1.0 - edgeThreshold) ||
                       fragTexCoord.y < edgeThreshold || fragTexCoord.y > (1.0 - edgeThreshold));
    
    // Only apply edge color detection near texture boundaries
    if (isNearEdge)
    {
        // Check alpha - discard semi-transparent edge pixels
        if (texColor.a < 0.95)
        {
            discard;
        }
        
        // Check for edge colors
        vec3 edgeColor1 = vec3(71.0/255.0, 208.0/255.0, 209.0/255.0);  // Light cyan edge
        float edgeDist1 = distance(texColor.rgb, edgeColor1);
        
        vec3 edgeColor2 = vec3(1.0, 1.0, 1.0); // Pure white edges
        float edgeDist2 = distance(texColor.rgb, edgeColor2);
        
        vec3 edgeColor3 = vec3(200.0/255.0, 240.0/255.0, 240.0/255.0); // Very light cyan
        float edgeDist3 = distance(texColor.rgb, edgeColor3);
        
        // Discard edge colors only at boundaries
        if (edgeDist1 < 0.1 || edgeDist2 < 0.1 || edgeDist3 < 0.1)
        {
            discard;
        }
        
        // Discard very bright pixels at edges
        float brightness = (texColor.r + texColor.g + texColor.b) / 3.0;
        if (brightness > 0.95)
        {
            discard;
        }
    }
    
    // Detect water pixels: cyan/blue with lower red than blue.
    float blueness = texColor.b;
    float greenness = texColor.g;
    float redness = texColor.r;
    
    // Water detection: cyan color profile
    if (blueness > 0.4 && greenness > 0.35 && redness < blueness * 0.7)
    {
        // World-space UVs in tile units.
        vec2 p = fragPosition / 32.0;

        float h;
        vec2 grad;
        evaluateAnisotropicWaves(p, time, h, grad);

        // Localized movement disturbance around player position on water.
        vec2 playerTilePos = playerWorldPos / 32.0;
        vec2 toPlayer = p - playerTilePos;
        float distToPlayer = max(length(toPlayer), 0.0001);
        float ripplePhase = distToPlayer * 12.0 - time * 2.0;
        float rippleAtten = exp(-distToPlayer * 2.4);
        float rippleAmp = 0.007 * clamp(playerRippleStrength, 0.0, 1.0);
        float rippleHeight = sin(ripplePhase) * rippleAtten * rippleAmp;
        h += rippleHeight;

        float rippleSlope = rippleAmp * rippleAtten * (12.0 * cos(ripplePhase) - 2.4 * sin(ripplePhase));
        grad += (toPlayer / distToPlayer) * rippleSlope;

        // Pseudo-normal from gradient.
        vec3 n = normalize(vec3(-grad.x, -grad.y, 1.0));

        // Keep base color anchored to the original water texel to avoid pulling
        // shoreline/dirt edge pixels into interior water when UVs are distorted.
        vec3 baseWater = texColor.rgb;

        // Depth tint approximation from wave height.
        float depthLerp = clamp(0.5 + h * 10.0, 0.0, 1.0);
        vec3 shallowColor = baseWater * vec3(1.08, 1.06, 1.02);
        vec3 deepColor = baseWater * vec3(0.70, 0.82, 0.95);
        vec3 waterColor = mix(deepColor, shallowColor, depthLerp);

        // Fresnel-style angular response for 3/4 camera.
        vec3 viewDir = normalize(vec3(0.0, -0.45, 0.89));
        float ndotv = max(0.0, dot(n, viewDir));
        float fresnel = F0 + (1.0 - F0) * pow(1.0 - ndotv, 5.0);

        // Reflection lookup — the reflection texture already contains flipped
        // sprites drawn at the correct reflection positions, so sample directly
        // at the current fragment's screen UV with mild wave distortion.
        vec2 reflectionUV = fragScreenUV + DISTORTION_STRENGTH * 0.7 * n.xy;
        reflectionUV = clamp(reflectionUV, 0.001, 0.999);
        // fragScreenUV is in GL convention (Y-up) which matches the render
        // texture's OpenGL coordinate system — no additional flip needed.
        vec4 reflectionSample = texture(reflectionTexture, reflectionUV);
        vec3 reflectionColor = reflectionSample.rgb;

        // Only show reflection where the texture has actual content (alpha > 0).
        bool allowReflection = reflectionSample.a > 0.03;

        // Wave highlights from slope and cresting.
        float slope = clamp(length(grad), 0.0, 1.0);
        float crest = pow(clamp(0.5 + h * 24.0, 0.0, 1.0), 3.0);
        vec3 specTint = vec3(0.92, 0.98, 1.0) * (0.12 + 0.38 * slope + 0.20 * crest);

        // Blend reflection by Fresnel and add a small specular tint.
        float reflectionStrength = allowReflection ? clamp(0.18 + fresnel * 0.62, 0.0, 0.80) : 0.0;
        waterColor = mix(waterColor, reflectionColor, reflectionStrength);
        waterColor += specTint * fresnel;
        waterColor = clamp(waterColor, 0.0, 1.0);

        float waterAlpha = clamp(BASE_WATER_ALPHA * texColor.a, 0.0, 1.0);
        finalColor = vec4(waterColor, waterAlpha) * colDiffuse * fragColor;
    }
    else
    {
        // Not a water pixel - render normally
        finalColor = texColor * colDiffuse * fragColor;
    }
}
