(function () {
    const baseaddr = window.location.origin + "/api/";

    let pageName;     // The name of the page the gallery is installed on (read from the active breadcrumb item)
    let breadcrumbEl; // Breadcrumb elements before the active item, kept for re-render on navigation
    let currentDir = null; // Tracks the active directory; stale async callbacks check this before acting
    let dataLoaded = false;

    /* When the page is loaded, kick off the gallery loading process */
    $(function () {
        pageName = $(".breadcrumb .active").text();
        breadcrumbEl = $(".breadcrumb").children(":not(.active)");

        load(window.location.hash.substring(1));
    });

    /* When the hash changes, reload the gallery for the new path */
    $(window).on("hashchange", function () {
        load(window.location.hash.substring(1));
    });

    /* Load the image index and display images on page */
    function load(dir) {
        if (dir.length === 0) { dir = "/"; }

        currentDir = dir;
        dataLoaded = false;

        $("#gallery").empty();
        $("#folders").empty();
        $(document).scrollTop(0);

        setTimeout(function () { getProgress(dir); }, 1000);
        getDirIndex(dir);
        generateBreadcrumb();
    }

    /* Handles the display of the loading progress bar */
    function getProgress(dir) {
        // Bail out if we've navigated away or the index already loaded
        if (dir !== currentDir || dataLoaded) { $("#progress").hide(); return; }

        $("#progress").show();
        $.getJSON(baseaddr + "dir/status" + dir, function (data) {
            const percent = data.percent;
            $(".progress-bar")
                .text("Creating thumbnails - " + percent + "%")
                .attr("aria-valuenow", percent)
                .css("width", percent + "%");
            $("#progress-status").text(data.currentWork || "");

            if (percent === 100) {
                $("#progress").hide();
                if (!dataLoaded) { getDirIndex(dir); }
            } else {
                setTimeout(function () { getProgress(dir); }, 1000);
            }
        }).fail(function () {
            // Any error (404, 500, etc.) — stop polling; getDirIndex will handle recovery
            $("#progress").hide();
        });
    }

    /* Escape a string for safe insertion into HTML */
    function escapeHtml(str) {
        return str
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }

    /* Load the dir index. The request stays open until all thumbnails are generated,
       so the progress bar runs async while this is waiting. */
    function getDirIndex(dir) {
        $.getJSON(baseaddr + "dir" + dir, function (data) {
            // Ignore stale responses from a previous navigation
            if (dir !== currentDir || data == null) { return; }
            dataLoaded = true;

            const folderHtml = [];
            const galleryHtml = [];

            for (const key in data) {
                const title = escapeHtml(data[key].title);
                if (data[key].type === "dir") {
                    folderHtml.push(
                        "<div class='gallery-folder'>" +
                        "<a href=\"#" + dir + encodeURIComponent(data[key].title) + "/\">" +
                        "<div><div><img src=\"" + baseaddr + "media/icon/" + encodeURI(key) + "\"></div></div>" +
                        "<span>" + title + "</span>" +
                        "</a></div>"
                    );
                } else if (data[key].type === "video") {
                    galleryHtml.push(
                        "<div class='gallery-item'>" +
                        "<a href=\"" + baseaddr + "media/file" + dir + encodeURI(key) + "\" data-fancybox='gallery' data-thumb=\"" + baseaddr + "media/icon" + dir + encodeURIComponent(key) + "\">" +
                        "<video autoplay loop muted playsinline>" +
                        "<source src=\"" + baseaddr + "media/thumb" + dir + encodeURI(key) + "\" type='video/mp4'>" +
                        "</video></a>" +
                        "<div class='gallery-video'><div>" + title + "</div></div>" +
                        "</div>"
                    );
                } else if (data[key].type === "image") {
                    galleryHtml.push(
                        "<div class='gallery-item'>" +
                        "<a href=\"" + baseaddr + "media/file" + dir + encodeURI(key) + "\" data-fancybox='gallery'>" +
                        "<img src=\"" + baseaddr + "media/thumb" + dir + encodeURI(key) + "\" alt=\"" + title + "\" />" +
                        "</a>" +
                        "<div class='gallery-image'><div>" + title + "</div></div>" +
                        "</div>"
                    );
                }
            }

            $("#folders").append(folderHtml.join(""));
            $("#gallery").append(galleryHtml.join(""));

            // Tear down any previous Fancybox binding before re-binding for the new directory
            Fancybox.unbind("[data-fancybox='gallery']");
            Fancybox.bind("[data-fancybox='gallery']", {
                Carousel: {
                    Thumbs: {
                        showOnStart: false,
                    },
                    formatCaption: function (carouselRef, slide) {
                        // Title from the gallery overlay div adjacent to the trigger <a>
                        const titleText = slide.triggerEl?.nextElementSibling?.textContent || "";
                        const title = titleText ? "<strong>" + escapeHtml(titleText) + "</strong><br />" : "";
                        const captionId = "fancybox-caption-" + slide.index;

                        // Async-load IPTC caption from per-file metadata ADS
                        $.getJSON(slide.src.replace("/media/file/", "/media/meta/"), function (data) {
                            const el = document.getElementById(captionId);
                            if (el && data.Caption) {
                                el.innerHTML = escapeHtml(data.Caption) + "<br />";
                            }
                        }).fail(function (jqXHR, textStatus, errorThrown) {
                            console.log(textStatus, errorThrown);
                        });

                        return title +
                            "<span id='" + captionId + "'></span>" +
                            "<a href='" + slide.src + "'>Download</a>";
                    }
                },
                hash: false
            });
        }).fail(function (jqXHR) {
            if (jqXHR.status === 409) {
                // Another generation is already in progress for this dir — retry after a short delay
                setTimeout(function () { getDirIndex(dir); }, 1000);
            } else {
                console.log("Failed to load directory index:", jqXHR.status);
            }
        });
    }

    /* Breadcrumb generation — customize the initial breadcrumb in Default.htm to match your site */
    function generateBreadcrumb() {
        $(".breadcrumb").html(breadcrumbEl);

        if (window.location.hash.length > 1) {
            $(".breadcrumb").append("<li><a href='" + window.location.pathname + "'>" + pageName + "</a>&nbsp;<span class='divider'>/</span></li>");
        } else {
            $(".breadcrumb").append("<li class='active'>" + pageName + "</li>");
        }

        const path = window.location.hash.substring(1).split("/");
        let pathCompile = "/";
        for (let i = 0; i < path.length; i++) {
            if (path[i].length === 0) { continue; }
            pathCompile += path[i] + "/";
            if (i === path.length - 2) {
                $(".breadcrumb").append("<li class='active'>" + escapeHtml(decodeURIComponent(path[i])) + "</li>");
            } else {
                $(".breadcrumb").append("<li><a href='" + window.location.pathname + "#" + pathCompile + "'>" + escapeHtml(decodeURIComponent(path[i])) + "</a>&nbsp;<span class='divider'>/</span></li>");
            }
        }
    }
})();
