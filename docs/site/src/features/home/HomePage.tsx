import "./hero.css"
import { Link } from "@tanstack/react-router"
import { ArrowRightIcon, BlocksIcon, BoxesIcon, CpuIcon, LayoutPanelTopIcon, PaletteIcon, SunIcon, type LucideIcon } from "lucide-react"
import { CodeBlock } from "@/common/components/CodeBlock"
import { t, type MessageKey } from "@/common/i18n/t"
import { getGuide, orderedGuides } from "@/features/guides/guides"
import { HomeFooter } from "./HomeFooter"

interface Feature {
  icon: LucideIcon
  title: MessageKey
  body: MessageKey
  guide: string
  fallbackType: string
}

const features: Feature[] = [
  { icon: BoxesIcon, title: "home.feature.ecs.title", body: "home.feature.ecs.body", guide: "ecs", fallbackType: "ECS" },
  { icon: BlocksIcon, title: "home.feature.systems.title", body: "home.feature.systems.body", guide: "systems", fallbackType: "System" },
  { icon: PaletteIcon, title: "home.feature.renderer.title", body: "home.feature.renderer.body", guide: "renderer", fallbackType: "Renderer" },
  { icon: SunIcon, title: "home.feature.lighting.title", body: "home.feature.lighting.body", guide: "lighting", fallbackType: "HRCGI" },
  { icon: LayoutPanelTopIcon, title: "home.feature.inspector.title", body: "home.feature.inspector.body", guide: "inspector", fallbackType: "Inspector" },
  { icon: CpuIcon, title: "home.feature.runtime.title", body: "home.feature.runtime.body", guide: "runtime-backends", fallbackType: "GameClient" },
]

/** Shown verbatim on the home page; kept in sync with the README quick start. */
const firstScene = `using System.Numerics;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.math;
using com.radiant.engine.runtime;

new GameClient().Run(new TriangleScene(), new WindowOptions { Title = "Triangles" });

public class TriangleScene : Scene
{
    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<Geometry>();
        var light = ECS.AddSystem<HRCGI>();
        var color = ECS.AddSystem(new ToneMapping
        {
            Input = light.GetOutput, Exposure = 2f,
            Mapper = ToneMapper.None, After = [light]
        });
        ECS.AddSystem(new RenderPresentation { Input = color.GetOutput, After = [color] });
    }

    public override void SetupScene()
    {
        var center = Renderer.VirtualSize / 2;
        Renderer.FitWorld(center, Renderer.VirtualSize);
        int triangle = ECS.CreateEntity();
        ECS.AddComponent<Transform2D>(triangle) = new()
        {
            Position = center - new Vector2(180), Scale = Vector2.One
        };
        ECS.AddComponent<Triangle2D>(triangle) = new() { Size = new Vector2(360), Bordered = true };
        ECS.AddComponent<Material>(triangle) = new()
        {
            Albedo = Color.Black, Emissive = new Color(220, 80, 255)
        };
        LightFactory.CreateLight(ECS, center + new Vector2(-400, 100), 50, new Color(0, 220, 255), new Color(0, 220, 255));
        LightFactory.CreateLight(ECS, center + new Vector2(400, 100), 50, Color.Orange, Color.Orange);
    }
}`

const featureCardClass = "group flex flex-col gap-2 rounded-xl border bg-card p-5 transition-colors hover:border-foreground/20 hover:bg-accent/40"

export function HomePage() {
  const firstGuide = getGuide("getting-started") ?? orderedGuides[0]

  return (
    <div id="content">
      <section className="radiant-hero" aria-labelledby="radiant-hero-title">
        <img
          className="radiant-hero__image"
          src={`${import.meta.env.BASE_URL}images/radiant-prism-studio.webp`}
          alt=""
          width="1672"
          height="941"
          fetchPriority="high"
        />
        <div className="radiant-hero__scrim" aria-hidden="true" />
        <div className="radiant-hero__inner">
          <div className="radiant-hero__copy">
            <h1 id="radiant-hero-title">{t("home.headline")}</h1>
            <p>{t("home.tagline")}</p>
            <div className="radiant-hero__actions">
              {firstGuide && (
                <Link to="/guides/$slug" params={{ slug: firstGuide.slug }} className="radiant-hero__primary">
                  {t("home.getStarted")} <ArrowRightIcon aria-hidden="true" size={18} />
                </Link>
              )}
              <Link to="/api" className="radiant-hero__secondary">
                {t("home.browseApi")} <ArrowRightIcon aria-hidden="true" size={18} />
              </Link>
            </div>
          </div>
        </div>
      </section>

      <section className="mx-auto max-w-6xl px-4 py-14 md:px-6">
        <h2 className="text-2xl font-semibold tracking-tight">{t("home.featuresTitle")}</h2>
        <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {features.map((feature) => {
            const content = (
              <>
                <feature.icon className="size-5 text-brand" />
                <h3 className="font-medium">{t(feature.title)}</h3>
                <p className="text-sm text-muted-foreground">{t(feature.body)}</p>
              </>
            )
            return getGuide(feature.guide) ? (
              <Link key={feature.title} to="/guides/$slug" params={{ slug: feature.guide }} className={featureCardClass}>
                {content}
              </Link>
            ) : (
              <Link key={feature.title} to="/api/$typeId" params={{ typeId: feature.fallbackType }} className={featureCardClass}>
                {content}
              </Link>
            )
          })}
        </div>
      </section>

      <section className="border-t">
        <div className="mx-auto grid grid-cols-1 max-w-6xl gap-8 px-4 py-14 md:px-6">
          <div className="space-y-3">
            <h2 className="text-2xl font-semibold tracking-tight">{t("home.firstScene.title")}</h2>
            <p className="text-muted-foreground">{t("home.firstScene.body")}</p>
            {firstGuide && (
              <Link to="/guides/$slug" params={{ slug: firstGuide.slug }} className="inline-flex items-center gap-1.5 text-sm font-medium hover:underline">
                {t("home.firstScene.more")} <ArrowRightIcon aria-hidden="true" size={14} />
              </Link>
            )}
          </div>
          <CodeBlock code={firstScene} language="csharp" className="my-0" />
        </div>
      </section>

      <HomeFooter />
    </div>
  )
}
