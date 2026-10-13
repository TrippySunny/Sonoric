(function () {
    var dialog = document.getElementById("demo-dialog");
    var openButton = document.getElementById("demo-open-button");
    var closeButton = document.getElementById("demo-close-button");

    if (!dialog || !openButton || !closeButton) {
        return;
    }

    openButton.addEventListener("click", function () {
        dialog.showModal();
    });

    closeButton.addEventListener("click", function () {
        dialog.close();
    });

    dialog.addEventListener("click", function (event) {
        if (event.target === dialog) {
            dialog.close();
        }
    });
})();
