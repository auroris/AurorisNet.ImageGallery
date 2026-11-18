import PhotoSwipeLightbox from "./lib/photoswipe-5.4.4/photoswipe-lightbox.esm.min.js";

export function createViewer(gallery) {
    const lightbox = new PhotoSwipeLightbox({
        gallery,
        children: "a:not(.original-video)",
        pswpModule: () => import("./lib/photoswipe-5.4.4/photoswipe.esm.min.js"),
        showHideAnimationType: "none",
        secondaryZoomLevel: 1,
        paddingFn: () => ({
            top: 60, bottom: lightbox.pswp?.element?.querySelector(".pswp__footer")?.offsetHeight || 60,
            left: 0, right: 0
        })
    });

    lightbox.on("contentLoad", event => {
        const { content } = event;
        if (content.type !== "video") return;
        event.preventDefault();
        const video = document.createElement("video");
        video.controls = true;
        video.playsInline = true;
        video.preload = "none";
        video.poster = content.data.element.dataset.icon;
        video.src = content.data.src;
        video.setAttribute("aria-label", content.data.element.dataset.title);
        content.element = document.createElement("div");
        content.element.className = "viewer-video";
        content.element.append(video);
    });
    lightbox.on("contentActivate", ({ content }) => {
        if (content.type === "video") {
            // Browsers that block autoplay still expose the native play control.
            content.element.querySelector("video").play().catch(() => {});
        }
    });
    for (const eventName of ["contentDeactivate", "contentRemove"]) {
        lightbox.on(eventName, ({ content }) => {
            if (content.type === "video") content.element?.querySelector("video")?.pause();
        });
    }
    // Let the native player handle playback, seeking and volume adjustments.
    lightbox.on("pointerDown", event => {
        if (event.originalEvent.target instanceof HTMLVideoElement) event.preventDefault();
    });
    lightbox.addFilter("preventPointerEvent", (prevent, event) =>
        event.target instanceof HTMLVideoElement ? false : prevent);
    lightbox.on("keydown", event => {
        if (event.originalEvent.target instanceof HTMLVideoElement && event.originalEvent.key !== "Escape") {
            event.preventDefault();
        }
    });
    lightbox.on("uiRegister", () => registerControls(lightbox.pswp));
    lightbox.init();
    return lightbox;
}

function registerControls(pswp) {
    let strip;
    let slideTimer;
    let playing = false;
    function scheduleNextSlide() {
        clearTimeout(slideTimer);
        if (playing) slideTimer = setTimeout(() => pswp.next(), 3000);
    }
    pswp.ui.registerElement({
        name: "footer", appendTo: "root",
        onInit(footer) {
            const title = document.createElement("strong");
            const caption = document.createElement("span");
            const download = document.createElement("a");
            download.textContent = "Download";
            const details = document.createElement("div");
            details.className = "viewer-details";
            details.append(title, caption, download);
            strip = document.createElement("div");
            strip.className = "viewer-thumbnails";
            strip.setAttribute("role", "group");
            strip.setAttribute("aria-label", "Thumbnails");
            strip.hidden = true;
            footer.append(details, strip);

            let captionRequest;
            pswp.on("change", async () => {
                captionRequest?.abort();
                const { element } = pswp.currSlide.data;
                title.textContent = element.dataset.title;
                caption.textContent = "";
                download.href = element.href;
                download.download = "";
                if (!strip.hidden) selectThumbnail(strip, pswp.currIndex);
                scheduleNextSlide();

                captionRequest = new AbortController();
                const { signal } = captionRequest;
                try {
                    const response = await fetch(element.dataset.meta, { signal });
                    if (!response.ok) return;
                    const metadata = await response.json();
                    if (!signal.aborted) caption.textContent = metadata.Caption || "";
                } catch (error) {
                    if (!signal.aborted) console.error("Failed to load caption", error);
                }
            });
            const observer = new ResizeObserver(() => {
                // Reserve room for the actual caption and optional thumbnail strip.
                pswp.element.style.setProperty("--viewer-footer-height", footer.offsetHeight + "px");
                pswp.updateSize(true);
            });
            observer.observe(footer);
            pswp.on("destroy", () => {
                observer.disconnect();
                captionRequest?.abort();
                clearTimeout(slideTimer);
            });
        }
    });

    if (pswp.getNumItems() > 1) {
        pswp.ui.registerElement({
            name: "slideshow", title: "Slideshow", order: 8, isButton: true, html: "▶",
            onInit(button) { button.setAttribute("aria-pressed", "false"); },
            onClick(_, button) {
                playing = !playing;
                button.setAttribute("aria-pressed", String(playing));
                button.textContent = playing ? "Ⅱ" : "▶";
                scheduleNextSlide();
            }
        });
        pswp.ui.registerElement({
            name: "thumbnails", title: "Thumbnails", order: 10, isButton: true, html: "▦",
            onInit(button) { button.setAttribute("aria-expanded", "false"); },
            onClick(_, button) {
                if (!strip.childElementCount) {
                    for (let index = 0; index < pswp.getNumItems(); index++) {
                        const { element } = pswp.getItemData(index);
                        const thumb = document.createElement("button");
                        thumb.type = "button";
                        thumb.setAttribute("aria-label", element.dataset.title);
                        const image = document.createElement("img");
                        image.loading = "lazy";
                        image.alt = "";
                        image.src = element.dataset.icon || element.querySelector("img").src;
                        thumb.append(image);
                        thumb.addEventListener("click", () => pswp.goTo(index));
                        strip.append(thumb);
                    }
                }
                strip.hidden = !strip.hidden;
                button.setAttribute("aria-expanded", String(!strip.hidden));
                if (!strip.hidden) selectThumbnail(strip, pswp.currIndex);
            }
        });
    }

    if (document.fullscreenEnabled) {
        pswp.ui.registerElement({
            name: "fullscreen", title: "Fullscreen", order: 9, isButton: true, html: "⛶",
            onInit(button) {
                const update = () => button.setAttribute("aria-pressed", String(document.fullscreenElement === pswp.element));
                update();
                document.addEventListener("fullscreenchange", update);
                pswp.on("destroy", () => document.removeEventListener("fullscreenchange", update));
            },
            async onClick() {
                try {
                    if (document.fullscreenElement === pswp.element) await document.exitFullscreen();
                    else await pswp.element.requestFullscreen();
                } catch (error) {
                    console.error("Failed to toggle fullscreen", error);
                }
            }
        });
    }
}

function selectThumbnail(strip, index) {
    strip.querySelector("[aria-current]")?.removeAttribute("aria-current");
    const selected = strip.children[index];
    selected.setAttribute("aria-current", "true");
    selected.scrollIntoView({ block: "nearest", inline: "nearest" });
}
