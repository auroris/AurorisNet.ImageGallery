import { createViewer } from "./Viewer.js";

const gallery = document.querySelector("#gallery");
const folders = document.querySelector("#folders");
const progress = document.querySelector("#progress");
const progressBar = progress.querySelector(".progress-bar");
const breadcrumb = document.querySelector(".breadcrumb");
const pageName = breadcrumb.querySelector(".active").textContent;
const breadcrumbPrefix = [...breadcrumb.children].filter(item => !item.classList.contains("active"));
let directoryRequest;
let viewer;

window.addEventListener("hashchange", loadDirectory);
loadDirectory();

async function loadDirectory() {
    directoryRequest?.abort();
    viewer?.destroy();
    viewer = null;
    const request = new AbortController();
    directoryRequest = request;
    const { signal } = request;
    const segments = location.hash.slice(1).split("/").filter(Boolean);
    const dir = "/" + (segments.length ? segments.join("/") + "/" : "");
    renderBreadcrumb(segments);
    gallery.replaceChildren();
    folders.replaceChildren();
    progress.style.display = "none";
    window.scrollTo(0, 0);

    const progressTimer = setTimeout(() => showProgress(dir, signal), 1000);
    try {
        // A previous visit may still have thumbnail generation in progress.
        let response;
        do {
            // Older, seven-day-cached responses predate the image dimensions used by PhotoSwipe.
            response = await fetch("/api/dir" + dir + "?dimensions=1", { signal });
            if (response.status === 409) {
                await new Promise(resolve => setTimeout(resolve, 1000));
            }
        } while (response.status === 409 && !signal.aborted);
        if (signal.aborted) return;
        if (!response.ok) throw new Error("Directory request returned " + response.status);
        const entries = await response.json();
        if (signal.aborted) return;
        for (const [key, item] of Object.entries(entries)) {
            if (item.type === "dir") {
                folders.append(folderCard(dir, key, item));
            } else if (item.type === "image" || item.type === "video") {
                gallery.append(mediaCard(dir, key, item));
            }
        }
        viewer = createViewer(gallery);
    } catch (error) {
        if (!signal.aborted) console.error("Failed to load directory", error);
    } finally {
        clearTimeout(progressTimer);
        if (!signal.aborted) progress.style.display = "none";
        request.abort();
    }
}

async function showProgress(dir, signal) {
    if (signal.aborted) return;
    try {
        const response = await fetch("/api/dir/status" + dir, { signal });
        if (!response.ok) return;
        const { percent, currentWork } = await response.json();
        if (signal.aborted) return;
        if (percent === 100) {
            progress.style.display = "none";
            return;
        }
        progress.style.display = "flex";
        progressBar.textContent = "Creating thumbnails - " + percent + "%";
        progressBar.setAttribute("aria-valuenow", percent);
        progressBar.style.width = percent + "%";
        document.querySelector("#progress-status").textContent = currentWork || "";
        setTimeout(() => showProgress(dir, signal), 1000);
    } catch (error) {
        if (!signal.aborted) console.error("Failed to load thumbnail progress", error);
    }
}

function folderCard(dir, key, item) {
    const card = document.createElement("div");
    card.className = "gallery-folder";
    const link = document.createElement("a");
    link.href = "#" + dir + encodeURIComponent(item.title) + "/";
    const face = document.createElement("div");
    if (!item.previewUnavailable) {
        const icon = document.createElement("img");
        icon.alt = "";
        icon.src = "/api/media/icon/" + key.split("/").map(encodeURIComponent).join("/");
        face.append(icon);
    }
    const folder = document.createElement("div");
    folder.append(face);
    const label = document.createElement("span");
    label.textContent = item.title;
    link.append(folder, label);
    card.append(link);
    return card;
}

function mediaCard(dir, key, item) {
    const path = dir + encodeURIComponent(key);
    const card = document.createElement("div");
    card.className = "gallery-item";
    const link = document.createElement("a");
    link.href = "/api/media/file" + path;
    link.dataset.title = item.title;
    link.dataset.meta = "/api/media/meta" + path;
    link.setAttribute("aria-label", item.title);
    if (item.previewUnavailable) {
        link.className = "original-video";
        link.textContent = "Open original video";
        link.setAttribute("aria-label", "Open original video " + item.title);
    } else if (item.type === "video") {
        link.dataset.pswpType = "video";
        link.dataset.icon = "/api/media/icon" + path;
        const preview = document.createElement("video");
        preview.muted = true;
        preview.loop = true;
        preview.autoplay = true;
        preview.playsInline = true;
        preview.poster = link.dataset.icon;
        preview.src = "/api/media/thumb" + path;
        link.append(preview);
    } else {
        if (item.width && item.height) {
            link.dataset.pswpWidth = item.width;
            link.dataset.pswpHeight = item.height;
        }
        const preview = document.createElement("img");
        preview.alt = item.title;
        preview.src = "/api/media/thumb" + path;
        link.append(preview);
    }

    const overlay = document.createElement("div");
    overlay.className = "gallery-" + item.type;
    const label = document.createElement("div");
    label.textContent = item.title;
    overlay.append(label);
    card.append(link, overlay);
    return card;
}

function renderBreadcrumb(segments) {
    breadcrumb.replaceChildren(...breadcrumbPrefix);
    const titles = [pageName, ...segments.map(segment => decodeURIComponent(segment))];
    for (const [index, title] of titles.entries()) {
        const item = document.createElement("li");
        if (index === titles.length - 1) {
            item.className = "active";
            item.textContent = title;
        } else {
            const link = document.createElement("a");
            link.href = index ? "#/" + segments.slice(0, index).join("/") + "/" : location.pathname;
            link.textContent = title;
            const divider = document.createElement("span");
            divider.className = "divider";
            divider.textContent = "/";
            item.append(link, "\u00a0", divider);
        }
        breadcrumb.append(item);
    }
}
