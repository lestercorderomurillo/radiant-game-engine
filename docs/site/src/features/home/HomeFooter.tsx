import "./footer.css"
import { Link } from "@tanstack/react-router"
import { Logo } from "@/common/components/Logo"
import { t } from "@/common/i18n/t"
import { getGuide } from "@/features/guides/guides"
import { useApiIndex } from "@/features/reference/useApiIndex"

const repository = "https://github.com/lestercorderomurillo/radiant-game-engine"
const guides = ["getting-started", "ecs", "systems", "renderer", "lighting", "inspector", "runtime-backends"]
  .flatMap((slug) => {
    const guide = getGuide(slug)
    return guide ? [guide] : []
  })
const linkClass = "radiant-footer__link"

export function HomeFooter() {
  const index = useApiIndex()

  return (
    <footer className="radiant-footer" aria-label={t("footer.label")}>
      <div className="radiant-footer__inner">
        <div className="radiant-footer__body">
          <div className="radiant-footer__brand">
            <Link to="/" className="radiant-footer__wordmark">
              <Logo className="size-7" />
              {t("home.title")}
            </Link>
          </div>
          <nav aria-label={t("footer.navigation")} className="radiant-footer__nav">
            <div>
              <h2 className="radiant-footer__heading">{t("footer.documentation")}</h2>
              <ul>
                {guides.map((guide) => (
                  <li key={guide.slug}>
                    <Link to="/guides/$slug" params={{ slug: guide.slug }} className={linkClass}>{guide.title}</Link>
                  </li>
                ))}
              </ul>
            </div>
            <div>
              <h2 className="radiant-footer__heading">{t("nav.api")}</h2>
              <ul>
                {index?.groups.map((group) => (
                  <li key={group.id}>
                    <Link to="/api" hash={group.id} className={`${linkClass} radiant-footer__api-link`}>
                      <span>{group.title}</span>
                      <span className="radiant-footer__count">{t("footer.typeCount", { count: group.areas.reduce((total, area) => total + area.types.length, 0) })}</span>
                    </Link>
                  </li>
                ))}
                <li><Link to="/api" className={linkClass}>{t("footer.allTypes")}</Link></li>
              </ul>
            </div>
            <div className="radiant-footer__project">
              <h2 className="radiant-footer__heading">{t("footer.project")}</h2>
              <ul>
                <li><a href={repository} className={linkClass}>{t("footer.github")}</a></li>
                <li><a href={`${repository}/issues`} className={linkClass}>{t("footer.issues")}</a></li>
                <li><a href={`${repository}/releases`} className={linkClass}>{t("footer.releases")}</a></li>
                <li><a href={`${repository}/commits/main/`} className={linkClass}>{t("footer.changelog")}</a></li>
              </ul>
            </div>
          </nav>
        </div>
        <div className="radiant-footer__bottom">
          <p className="radiant-footer__credit">{t("footer.credit")}</p>
          <a href="#content" className={linkClass}>{t("footer.backToTop")}</a>
        </div>
      </div>
    </footer>
  )
}
