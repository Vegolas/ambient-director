import { ItemView, Platform, WorkspaceLeaf, setIcon } from "obsidian";
import type SceneMakerPlugin from "./main";

export const PANEL_VIEW_TYPE = "rpg-scene-maker-panel";

/** Height of the little toolbar above the embedded panel, in px. */
const BAR_HEIGHT = 34;

/** Element that behaves like both an <iframe> and an Electron <webview> for our purposes. */
type EmbedEl = HTMLElement & { src?: string; reload?: () => void };

/**
 * Hosts the full control panel inside an Obsidian pane, so scenes can be driven from the same
 * window as the session notes (handy on a laptop). On desktop it uses an Electron <webview>
 * (bypasses mixed-content / frame restrictions for the LAN http server); on mobile it falls back
 * to an <iframe>. A small toolbar can reload it or pop it out to the system browser.
 */
export class PanelView extends ItemView {
  private frame: EmbedEl | null = null;

  constructor(
    leaf: WorkspaceLeaf,
    private plugin: SceneMakerPlugin,
  ) {
    super(leaf);
  }

  getViewType(): string {
    return PANEL_VIEW_TYPE;
  }

  getDisplayText(): string {
    return "RPG Scene Maker";
  }

  getIcon(): string {
    return "dice-6";
  }

  async onOpen(): Promise<void> {
    this.render();
  }

  async onClose(): Promise<void> {
    this.frame = null;
  }

  private render(): void {
    const root = this.contentEl;
    root.empty();
    root.addClass("sm-panel-view");
    // Layout is set inline (not just in styles.css) so the pane fills correctly even if an older
    // styles.css is still installed — the <webview> otherwise collapses to its 150px intrinsic height.
    Object.assign(root.style, { position: "relative", height: "100%", padding: "0", overflow: "hidden" });

    const base = this.plugin.settings.baseUrl?.trim();
    if (!base) {
      root.createDiv({
        cls: "sm-panel-empty",
        text: "Set the server address in RPG Scene Maker settings, then reopen this panel.",
      });
      return;
    }

    const bar = root.createDiv({ cls: "sm-panel-bar" });
    Object.assign(bar.style, { position: "absolute", top: "0", left: "0", right: "0", height: `${BAR_HEIGHT}px` });
    bar.createSpan({ cls: "sm-panel-title", text: "Control panel" });
    bar.createSpan({ cls: "sm-panel-spacer" });

    const reload = bar.createEl("button", { cls: "sm-panel-btn", attr: { "aria-label": "Reload" } });
    setIcon(reload, "rotate-ccw");
    reload.onclick = () => this.reload();

    const external = bar.createEl("button", { cls: "sm-panel-btn", attr: { "aria-label": "Open in browser" } });
    setIcon(external, "external-link");
    external.onclick = () => window.open(base, "_blank");

    const host = root.createDiv({ cls: "sm-panel-frame" });
    Object.assign(host.style, { position: "absolute", top: `${BAR_HEIGHT}px`, left: "0", right: "0", bottom: "0" });
    this.frame = this.buildEmbed(host, base);
  }

  private buildEmbed(host: HTMLElement, url: string): EmbedEl {
    const fill = { position: "absolute", inset: "0", width: "100%", height: "100%", border: "0", display: "block" };
    if (Platform.isDesktopApp) {
      // <webview> isn't in the DOM typings; create it manually and treat it as an EmbedEl.
      const webview = document.createElement("webview") as EmbedEl;
      webview.addClass("sm-panel-embed");
      webview.setAttribute("src", url);
      webview.setAttribute("allowpopups", "");
      Object.assign(webview.style, fill);
      host.appendChild(webview);
      return webview;
    }
    const iframe = host.createEl("iframe", { cls: "sm-panel-embed" });
    Object.assign(iframe.style, fill);
    iframe.src = url;
    return iframe;
  }

  private reload(): void {
    const base = this.plugin.settings.baseUrl?.trim();
    if (!base || !this.frame) return;
    if (typeof this.frame.reload === "function") this.frame.reload();
    else this.frame.src = base; // iframe: reassigning src reloads it
  }
}
