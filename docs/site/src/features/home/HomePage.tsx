import "./hero.css"
import { Link } from "@tanstack/react-router"
import { ArrowRightIcon, BlocksIcon, BoxesIcon, CpuIcon, LayoutPanelTopIcon, PaletteIcon, SunIcon, type LucideIcon } from "lucide-react"
import { CodeBlock } from "@/common/components/CodeBlock"
import { t, type MessageKey } from "@/common/i18n/t"
import { getGuide, orderedGuides } from "@/features/guides/guides"
import { useApiIndex } from "@/features/reference/useApiIndex"

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
  { icon: SunIcon, title: "home.feature.lighting.title", body: "home.feature.lighting.body", guide: "hrc", fallbackType: "HRCGI" },
  { icon: LayoutPanelTopIcon, title: "home.feature.inspector.title", body: "home.feature.inspector.body", guide: "inspector", fallbackType: "Inspector" },
  { icon: CpuIcon, title: "home.feature.runtime.title", body: "home.feature.runtime.body", guide: "runtime-backends", fallbackType: "GameClient" },
]

/** Shown verbatim on the home page; kept in sync with the README quick start. */
const firstScene = `using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.math;
using com.radiant.engine.runtime;

new GameClient().Run(new Campfire(), new WindowOptions { Title = "Campfire" });

public class Campfire : Scene
{
    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<Geometry>();
        ECS.AddSystem<HRCGI>();
        base.SetupECS();
    }

    public override void SetupScene()
    {
        var warm = new Color(255, 180, 100);
        LightFactory.CreateLight(ECS, Renderer.VirtualSize / 2, 100f, warm, warm);
        base.SetupScene();
    }
}`

const featureCardClass = "group flex flex-col gap-2 rounded-xl border bg-card p-5 transition-colors hover:border-foreground/20 hover:bg-accent/40"

export function HomePage() {
  const index = useApiIndex()
  const firstGuide = getGuide("getting-started") ?? orderedGuides[0]

  return (
    <div id="content">
      <section className="radiant-hero" aria-labelledby="radiant-hero-title">
        <img
          className="radiant-hero__image"
          src={`${import.meta.env.BASE_URL}images/radiant-sky-cube.webp`}
          alt=""
          width="1672"
          height="941"
          fetchPriority="high"
        />
        <div className="radiant-hero__scrim" aria-hidden="true" />
        <div className="radiant-hero__inner">
          <div className="radiant-hero__copy">
            <h1 id="radiant-hero-title">{t("home.title")}</h1>
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
        <div className="mx-auto grid max-w-6xl gap-8 px-4 py-14 md:px-6 lg:grid-cols-[1fr_1.4fr] lg:items-center">
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

      <section className="border-t bg-muted/30">
        <div className="mx-auto max-w-6xl px-4 py-14 md:px-6">
          <h2 className="text-2xl font-semibold tracking-tight">{t("home.apiTitle")}</h2>
          <p className="mt-2 text-muted-foreground">{index ? t("home.apiBody") : t("home.noData")}</p>
          {index && (
            <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              {index.groups.map((group) => (
                <Link key={group.id} to="/api" hash={group.id} className="flex flex-col gap-3 rounded-xl border bg-card p-5 transition-colors hover:border-foreground/20">
                  <div className="flex items-baseline justify-between">
                    <h3 className="font-medium">{group.title}</h3>
                    <span className="text-xs text-muted-foreground">{t("home.typeCount", { count: group.areas.reduce((total, area) => total + area.types.length, 0) })}</span>
                  </div>
                  <div className="flex flex-wrap gap-1.5">
                    {group.areas.flatMap((area) => area.types).slice(0, 6).map((typeId) => (
                      <span key={typeId} className="rounded-md bg-muted px-1.5 py-0.5 font-mono text-[11px] text-muted-foreground">
                        {index.types.find((type) => type.id === typeId)?.name ?? typeId}
                      </span>
                    ))}
                  </div>
                </Link>
              ))}
            </div>
          )}
        </div>
      </section>
    </div>
  )
}
