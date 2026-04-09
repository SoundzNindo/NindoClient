#version 330

// Input vertex attributes
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;
in vec4 vertexColor;

// Input uniform values
uniform mat4 mvp;

// Output vertex attributes (to fragment shader)
out vec2 fragTexCoord;
out vec4 fragColor;
out vec2 fragPosition;
out vec2 fragScreenUV;

void main()
{
    // Send vertex attributes to fragment shader
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    fragPosition = vertexPosition.xy; // Pass world position to fragment shader

    // Calculate final vertex position and pass normalized screen UV for reflection lookup.
    vec4 clipPos = mvp * vec4(vertexPosition, 1.0);
    gl_Position = clipPos;
    vec2 ndc = clipPos.xy / clipPos.w;
    fragScreenUV = ndc * 0.5 + 0.5;
}
