import { Link } from "@tanstack/react-router"
import { Button } from "@/common/ui/button"
import { t } from "@/common/i18n/t"

export function NotFoundPage() {
  return (
    <div className="mx-auto flex max-w-md flex-col items-center gap-4 px-4 py-24 text-center">
      <p className="font-mono text-sm text-muted-foreground">404</p>
      <h1 className="text-2xl font-semibold tracking-tight">{t("error.notFound")}</h1>
      <p className="text-muted-foreground">{t("error.notFoundBody")}</p>
      <Button asChild variant="outline">
        <Link to="/">{t("error.home")}</Link>
      </Button>
    </div>
  )
}
