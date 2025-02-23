#[vertex]
#version 450

layout(location=0) in vec3 position;
layout(location=1) in vec2 uv;

layout(location=0) out vec2 frag_uv;

layout(set=0, binding=0) uniform Transform {
    mat4 matrix;
};

void main() {
    frag_uv = uv;
    gl_Position = matrix * vec4(position,1.0);
}


#[fragment]
#version 450

layout(location=0) in vec2 frag_uv;

layout(location=0) out vec4 frag_color;

layout(set=0, binding=1) uniform sampler2D tex;

void main() {
    frag_color = texture(tex, frag_uv);
}
