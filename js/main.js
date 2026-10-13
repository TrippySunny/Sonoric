(function () {
    var form = document.getElementById("feedback");

    if (!form) {
        return;
    }

    form.addEventListener("submit", function (event) {
        event.preventDefault();

        var locale = document.documentElement.lang === "en" ? "en" : "ru";
        var name = form.elements.name.value.trim();
        var topic = form.elements.topic.value;
        var message = form.elements.message.value.trim();
        var isBug = topic === "bug";
        var prefix = isBug
            ? (locale === "en" ? "Bug: " : "Баг: ")
            : (locale === "en" ? "Feature: " : "Идея: ");
        var summary = message.split("\n")[0].slice(0, 72);
        var title = prefix + (summary || (isBug ? "report" : "idea"));
        var body = locale === "en"
            ? [
                "## Details",
                message,
                "",
                "- Name: " + name
            ]
            : [
                "## Описание",
                message,
                "",
                "- Имя: " + name
            ];

        var url = "https://github.com/TrippySunny/Sonoric/issues/new"
            + "?labels=" + encodeURIComponent(isBug ? "bug" : "enhancement")
            + "&title=" + encodeURIComponent(title)
            + "&body=" + encodeURIComponent(body.join("\n"));

        window.location.href = url;
    });
})();
