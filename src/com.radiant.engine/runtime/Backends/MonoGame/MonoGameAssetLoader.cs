using com.radiant.engine.graphics;
using Microsoft.Xna.Framework.Content;
using Native = Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.runtime;

internal sealed class MonoGameAssetLoader(ContentManager content) : IGraphicsAssetLoader
{
    public Texture2D LoadTexture(string name) => new MonoGameTexture(content.Load<Native.Texture2D>(name), ownsNative: false);
    public Effect LoadEffect(string name) => new MonoGameEffect(content.Load<Native.Effect>(name).Clone());
}
