import { ItemView, Platform, WorkspaceLeaf, setIcon } from "obsidian";
import type SceneMakerPlugin from "./main";

export const PANEL_VIEW_TYPE = "rpg-scene-maker-panel";

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

    const base = this.plugin.settings.baseUrl?.trim();
    if (!base) {
      root.createDiv({
        cls: "sm-panel-empty",
        text: "Set the server address in RPG Scene Maker settings, then reopen this panel.",
      });
      return;
    }

    const bar = root.createDiv({ cls: "sm-panel-bar" });
    bar.createSpan({ cls: "sm-panel-title", text: "Control panel" });
    bar.createSpan({ cls: "sm-panel-spacer" });

    const reload = bar.createEl("button", { cls: "sm-panel-btn", attr: { "aria-label": "Reload" } });
    setIcon(reload, "rotate-ccw");
    reload.onclick = () => this.reload();

    const external = bar.createEl("button", { cls: "sm-panel-btn", attr: { "aria-label": "Open in browser" } });
    setIcon(external, "external-link");
    external.onclick = () => window.open(base, "_blank");

    const host = root.createDiv({ cls: "sm-panel-frame" });
    this.frame = this.buildEmbed(host, base);
  }

  private buildEmbed(host: HTMLElement, url: string): EmbedEl {
    if (Platform.isDesktopApp) {
      // <webview> isn't in the DOM typings; create it manually and treat it as an EmbedEl.
      const webview = document.createElement("webview") as EmbedEl;
      webview.addClass("sm-panel-embed");
      webview.setAttribute("src", url);
      webview.setAttribute("allowpopups", "");
      host.appendChild(webview);
      return webview;
    }
    const iframe = host.createEl("iframe", { cls: "sm-panel-embed" });
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
