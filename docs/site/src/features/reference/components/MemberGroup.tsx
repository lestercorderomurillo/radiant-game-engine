import { HashIcon } from "lucide-react"
import { DocAnchor } from "@/common/components/DocAnchor"
import { Markdown } from "@/common/components/Markdown"
import { Badge } from "@/common/ui/badge"
import { t } from "@/common/i18n/t"
import { resolveApiRef } from "@/common/lib/docLinks"
import type { ApiLink, ApiMember, ApiMeta } from "../reference.api"
import { KindIcon } from "./KindIcon"
import { ParameterList } from "./ParameterList"
import { SignatureBlock, Tokens } from "./Signature"
import { SourceLinks } from "./SourceLinks"

interface MemberGroupProps {
  anchor: string
  overloads: ApiMember[]
  meta?: ApiMeta
}

/** One member name with all of its overloads. */
export function MemberGroup({ anchor, overloads, meta }: MemberGroupProps) {
  const first = overloads[0]

  return (
    <div id={anchor} className="group/member scroll-mt-28 border-b py-6 last:border-b-0">
      <h3 className="flex flex-wrap items-center gap-2">
        <KindIcon kind={first.kind} />
        <a href={`#${anchor}`} className="font-mono text-[15px] font-semibold break-all hover:underline">
          {first.name}
        </a>
        <HashIcon className="size-3.5 text-muted-foreground opacity-0 transition-opacity group-hover/member:opacity-100" />
        {first.modifiers?.map((modifier) => (
          <Badge key={modifier} variant="secondary" className="font-mono text-[11px]">
            {modifier}
          </Badge>
        ))}
        {overloads.length > 1 && (
          <Badge variant="outline" className="text-[11px]">
            {t("reference.overloads", { count: overloads.length })}
          </Badge>
        )}
      </h3>

      <div className="mt-3 space-y-6">
        {overloads.map((member) => (
          <MemberDetail key={member.id} member={member} meta={meta} anchored={overloads.length > 1} />
        ))}
      </div>
    </div>
  )
}

function MemberDetail({ member, meta, anchored }: { member: ApiMember; meta?: ApiMeta; anchored: boolean }) {
  return (
    <div id={anchored ? member.id : undefined} className="scroll-mt-28 space-y-3">
      <SignatureBlock tokens={member.signature} />

      {member.obsolete && (
        <p className="text-sm text-destructive">
          <Badge variant="destructive" className="mr-2">
            {t("reference.obsolete")}
          </Badge>
          {member.obsolete}
        </p>
      )}

      {member.summary && <Markdown source={member.summary} variant="compact" />}

      {/* Names, types and defaults are already in the signature; list parameters only when documented. */}
      {member.parameters?.some((parameter) => parameter.description) ? <ParameterList parameters={member.parameters} /> : null}
      {member.typeParameters?.length ? <ParameterList title={t("reference.typeParameters")} parameters={member.typeParameters} /> : null}

      {member.returns && (
        <div className="space-y-1">
          <h4 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t("reference.returns")}</h4>
          <div className="flex flex-wrap items-baseline gap-x-2 text-sm">
            {member.returnType && <Tokens tokens={member.returnType} className="text-xs" />}
            <Markdown source={member.returns} variant="compact" className="text-muted-foreground" />
          </div>
        </div>
      )}

      {member.value && <Section title={t("reference.value")} markdown={member.value} />}
      {member.remarks && <Section title={t("reference.remarks")} markdown={member.remarks} />}

      {member.exceptions?.length ? (
        <div className="space-y-1">
          <h4 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t("reference.exceptions")}</h4>
          <ul className="space-y-1 text-sm">
            {member.exceptions.map((exception, index) => (
              <li key={index} className="flex flex-wrap gap-2">
                {exception.type && <LinkText link={exception.type} />}
                {exception.description && <Markdown source={exception.description} variant="compact" className="text-muted-foreground" />}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {member.examples?.map((example, index) => <Section key={index} title={t("reference.example")} markdown={example} />)}

      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
        {member.overrides && (
          <span>
            {t("reference.overrides")} <LinkText link={member.overrides} />
          </span>
        )}
        {member.implements?.map((implemented) => (
          <span key={implemented.text}>
            {t("reference.implementsMember")} <LinkText link={implemented} />
          </span>
        ))}
        {member.source && <SourceLinks meta={meta} source={member.source} className="ml-auto" />}
      </div>
    </div>
  )
}

function Section({ title, markdown }: { title: string; markdown: string }) {
  return (
    <div className="space-y-1">
      <h4 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{title}</h4>
      <Markdown source={markdown} variant="compact" />
    </div>
  )
}

function LinkText({ link }: { link: ApiLink }) {
  return link.ref ? (
    <DocAnchor link={resolveApiRef(link.ref)} className="font-mono text-foreground underline-offset-2 hover:underline">
      {link.text}
    </DocAnchor>
  ) : (
    <code className="font-mono">{link.text}</code>
  )
}
