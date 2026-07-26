package wordtoolkit.uno;

import com.sun.star.beans.PropertyValue;
import com.sun.star.bridge.XUnoUrlResolver;
import com.sun.star.comp.helper.Bootstrap;
import com.sun.star.document.MacroExecMode;
import com.sun.star.document.UpdateDocMode;
import com.sun.star.frame.XComponentLoader;
import com.sun.star.frame.XDesktop2;
import com.sun.star.frame.XStorable;
import com.sun.star.lang.XComponent;
import com.sun.star.lang.XServiceInfo;
import com.sun.star.uno.UnoRuntime;
import com.sun.star.uno.XComponentContext;
import com.sun.star.util.XCloseable;

import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.EOFException;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;

/**
 * One-shot, non-interactive UNO bridge used by WordToolkit's out-of-process
 * LibreOffice adapter. Document and output URLs arrive only over standard input;
 * the process command line contains no document path.
 */
public final class LibreOfficeUnoRender {
    private static final int REQUEST_MAGIC = 0x57545531; // WTU1
    private static final int RESPONSE_MAGIC = 0x57545231; // WTR1
    private static final int PROTOCOL_VERSION = 1;
    private static final int MAX_STRING_BYTES = 131_072;
    private static final int MAX_CONNECT_TIMEOUT_MILLISECONDS = 30_000;

    private LibreOfficeUnoRender() {
    }

    public static void main(String[] args) {
        if (args.length != 0) {
            writeTerminalFailure("PROTOCOL_ERROR", false, false, false);
            Runtime.getRuntime().halt(64);
            return;
        }

        try {
            Request request = readRequest(System.in);
            Result result = execute(request);
            writeSuccess(System.out, result);
            // The UNO Java bridge loads native URE libraries into this disposable
            // worker. Some supported Windows combinations crash during JVM shutdown
            // after the remote office was already closed. All owned UNO resources are
            // closed above, so terminate the one-shot worker without running native
            // shutdown hooks or finalizers. The parent still requires this exact zero
            // exit plus the complete protocol response and office-process exit.
            Runtime.getRuntime().halt(0);
        } catch (BridgeFailure failure) {
            writeTerminalFailure(
                failure.code,
                failure.documentClosed,
                failure.desktopTerminated,
                failure.localContextReleaseDeferredToProcessExit
            );
            Runtime.getRuntime().halt(65);
        } catch (Throwable failure) {
            writeTerminalFailure("INTERNAL_ERROR", false, false, false);
            Runtime.getRuntime().halt(70);
        }
    }

    private static Result execute(Request request) throws BridgeFailure {
        XComponentContext localContext = null;
        XDesktop2 desktop = null;
        XComponent document = null;
        boolean documentClosed = false;
        boolean desktopTerminated = false;
        String stage = "LOCAL_CONTEXT";

        try {
            localContext = Bootstrap.createInitialComponentContext(null);
            if (localContext == null || localContext.getServiceManager() == null) {
                throw new BridgeFailure("LOCAL_CONTEXT_FAILED");
            }

            stage = "CONNECT";
            Object resolverObject = localContext.getServiceManager().createInstanceWithContext(
                "com.sun.star.bridge.UnoUrlResolver",
                localContext
            );
            XUnoUrlResolver resolver = UnoRuntime.queryInterface(
                XUnoUrlResolver.class,
                resolverObject
            );
            if (resolver == null) {
                throw new BridgeFailure("CONNECT_FAILED");
            }

            XComponentContext remoteContext = connect(
                resolver,
                request.pipeName,
                request.connectTimeoutMilliseconds
            );
            Object desktopObject = remoteContext.getServiceManager().createInstanceWithContext(
                "com.sun.star.frame.Desktop",
                remoteContext
            );
            desktop = UnoRuntime.queryInterface(XDesktop2.class, desktopObject);
            XComponentLoader loader = UnoRuntime.queryInterface(
                XComponentLoader.class,
                desktopObject
            );
            if (desktop == null || loader == null) {
                throw new BridgeFailure("CONNECT_FAILED");
            }

            stage = "LOAD";
            PropertyValue[] loadProperties = new PropertyValue[] {
                property("Hidden", Boolean.TRUE),
                property("ReadOnly", Boolean.TRUE),
                property("AsTemplate", Boolean.FALSE),
                property("PickListEntry", Boolean.FALSE),
                property("RepairPackage", Boolean.FALSE),
                property("MacroExecutionMode", Short.valueOf(MacroExecMode.NEVER_EXECUTE)),
                property("UpdateDocMode", Short.valueOf(UpdateDocMode.NO_UPDATE)),
                property("FilterName", request.inputFilterName)
            };
            document = loader.loadComponentFromURL(
                request.sourceUrl,
                "_blank",
                0,
                loadProperties
            );
            if (document == null) {
                throw new BridgeFailure("LOAD_FAILED");
            }

            XServiceInfo serviceInfo = UnoRuntime.queryInterface(
                XServiceInfo.class,
                document
            );
            if (serviceInfo == null
                || !serviceInfo.supportsService("com.sun.star.text.TextDocument")) {
                throw new BridgeFailure("NOT_WRITER_DOCUMENT");
            }
            XStorable storable = UnoRuntime.queryInterface(XStorable.class, document);
            if (storable == null || !storable.isReadonly()) {
                throw new BridgeFailure("READ_ONLY_NOT_VERIFIED");
            }
            if (!storable.hasLocation()
                || !request.sourceUrl.equals(storable.getLocation())) {
                throw new BridgeFailure("SOURCE_LOCATION_NOT_PRESERVED");
            }

            stage = "EXPORT";
            PropertyValue[] filterData = buildPdfFilterData(request);
            PropertyValue[] exportProperties = new PropertyValue[] {
                property("FilterName", "writer_pdf_Export"),
                property("Overwrite", Boolean.FALSE),
                property("FilterData", filterData)
            };
            storable.storeToURL(request.outputUrl, exportProperties);
            if (!storable.hasLocation()
                || !request.sourceUrl.equals(storable.getLocation())) {
                throw new BridgeFailure("SOURCE_LOCATION_NOT_PRESERVED");
            }

            stage = "CLOSE";
            XCloseable closeable = UnoRuntime.queryInterface(XCloseable.class, document);
            if (closeable == null) {
                throw new BridgeFailure("CLOSE_FAILED");
            }
            closeable.close(false);
            documentClosed = true;
            document = null;

            stage = "TERMINATE";
            if (!desktop.terminate()) {
                throw new BridgeFailure("TERMINATE_FAILED");
            }
            desktopTerminated = true;
            desktop = null;

            return new Result(
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                documentClosed,
                desktopTerminated,
                true
            );
        } catch (BridgeFailure failure) {
            Cleanup cleanup = cleanup(document, desktop, localContext);
            throw failure.withCleanup(
                documentClosed || cleanup.documentClosed,
                desktopTerminated || cleanup.desktopTerminated,
                true
            );
        } catch (Throwable failure) {
            Cleanup cleanup = cleanup(document, desktop, localContext);
            String code;
            switch (stage) {
                case "LOCAL_CONTEXT":
                    code = "LOCAL_CONTEXT_FAILED";
                    break;
                case "CONNECT":
                    code = "CONNECT_FAILED";
                    break;
                case "LOAD":
                    code = "LOAD_FAILED";
                    break;
                case "EXPORT":
                    code = "EXPORT_FAILED";
                    break;
                case "CLOSE":
                    code = "CLOSE_FAILED";
                    break;
                case "TERMINATE":
                    code = "TERMINATE_FAILED";
                    break;
                default:
                    code = "INTERNAL_ERROR";
                    break;
            }
            throw new BridgeFailure(
                code,
                documentClosed || cleanup.documentClosed,
                desktopTerminated || cleanup.desktopTerminated,
                true
            );
        }
    }

    private static XComponentContext connect(
        XUnoUrlResolver resolver,
        String pipeName,
        int timeoutMilliseconds
    ) throws BridgeFailure {
        long deadline = System.nanoTime() + timeoutMilliseconds * 1_000_000L;
        String unoUrl = "uno:pipe,name=" + pipeName
            + ";urp;StarOffice.ComponentContext";
        while (true) {
            try {
                Object resolved = resolver.resolve(unoUrl);
                XComponentContext context = UnoRuntime.queryInterface(
                    XComponentContext.class,
                    resolved
                );
                if (context != null && context.getServiceManager() != null) {
                    return context;
                }
            } catch (com.sun.star.connection.NoConnectException failure) {
                // The exact office child is still starting. Retry only until the
                // caller-provided bounded deadline.
            } catch (com.sun.star.connection.ConnectionSetupException failure) {
                throw new BridgeFailure("CONNECT_FAILED");
            } catch (com.sun.star.lang.IllegalArgumentException failure) {
                throw new BridgeFailure("CONNECT_FAILED");
            }

            if (System.nanoTime() >= deadline) {
                throw new BridgeFailure("CONNECT_TIMEOUT");
            }
            try {
                Thread.sleep(50L);
            } catch (InterruptedException failure) {
                Thread.currentThread().interrupt();
                throw new BridgeFailure("CONNECT_CANCELLED");
            }
        }
    }

    private static PropertyValue[] buildPdfFilterData(Request request) {
        int count = request.pageRange.isEmpty() ? 7 : 8;
        PropertyValue[] values = new PropertyValue[count];
        int index = 0;
        values[index++] = property("UseLosslessCompression", Boolean.TRUE);
        values[index++] = property("ReduceImageResolution", Boolean.FALSE);
        values[index++] = property("UseTaggedPDF", Boolean.TRUE);
        values[index++] = property("ExportFormFields", Boolean.TRUE);
        values[index++] = property("ExportBookmarks", request.exportBookmarks);
        values[index++] = property("IsAddStream", Boolean.FALSE);
        values[index++] = property(
            "SelectPdfVersion",
            Long.valueOf(request.pdfA1b ? 1L : 0L)
        );
        if (!request.pageRange.isEmpty()) {
            values[index] = property("PageRange", request.pageRange);
        }
        return values;
    }

    private static PropertyValue property(String name, Object value) {
        PropertyValue property = new PropertyValue();
        property.Name = name;
        property.Value = value;
        return property;
    }

    private static Cleanup cleanup(
        XComponent document,
        XDesktop2 desktop,
        XComponentContext localContext
    ) {
        boolean documentClosed = document == null;
        boolean desktopTerminated = desktop == null;

        if (document != null) {
            try {
                XCloseable closeable = UnoRuntime.queryInterface(XCloseable.class, document);
                if (closeable != null) {
                    closeable.close(false);
                    documentClosed = true;
                }
            } catch (Throwable ignored) {
                try {
                    document.dispose();
                } catch (Throwable ignoredAgain) {
                    // Cleanup status remains false and the parent will fail closed.
                }
            }
        }
        if (desktop != null) {
            try {
                desktopTerminated = desktop.terminate();
            } catch (Throwable ignored) {
                // Cleanup status remains false and the parent will kill the process tree.
            }
        }
        return new Cleanup(documentClosed, desktopTerminated);
    }

    private static Request readRequest(InputStream input) throws BridgeFailure {
        try {
            DataInputStream stream = new DataInputStream(new BufferedInputStream(input));
            if (stream.readInt() != REQUEST_MAGIC || stream.readInt() != PROTOCOL_VERSION) {
                throw new BridgeFailure("PROTOCOL_ERROR");
            }
            String pipeName = readString(stream);
            String sourceUrl = readString(stream);
            String outputUrl = readString(stream);
            String inputFilterName = readString(stream);
            String pageRange = readString(stream);
            boolean pdfA1b = stream.readBoolean();
            boolean exportBookmarks = stream.readBoolean();
            int connectTimeoutMilliseconds = stream.readInt();
            if (stream.read() != -1) {
                throw new BridgeFailure("PROTOCOL_ERROR");
            }
            validateToken(pipeName, 128);
            validateUrl(sourceUrl);
            validateUrl(outputUrl);
            if (!inputFilterName.equals("Office Open XML Text")
                && !inputFilterName.equals("Office Open XML Text Template")) {
                throw new BridgeFailure("PROTOCOL_ERROR");
            }
            if (!pageRange.isEmpty()
                && !pageRange.matches("[1-9][0-9]{0,4}-[1-9][0-9]{0,4}")) {
                throw new BridgeFailure("PROTOCOL_ERROR");
            }
            if (connectTimeoutMilliseconds < 1_000
                || connectTimeoutMilliseconds > MAX_CONNECT_TIMEOUT_MILLISECONDS) {
                throw new BridgeFailure("PROTOCOL_ERROR");
            }
            return new Request(
                pipeName,
                sourceUrl,
                outputUrl,
                inputFilterName,
                pageRange,
                pdfA1b,
                exportBookmarks,
                connectTimeoutMilliseconds
            );
        } catch (BridgeFailure failure) {
            throw failure;
        } catch (EOFException failure) {
            throw new BridgeFailure("PROTOCOL_ERROR");
        } catch (IOException failure) {
            throw new BridgeFailure("PROTOCOL_ERROR");
        }
    }

    private static String readString(DataInputStream stream) throws IOException, BridgeFailure {
        int length = stream.readInt();
        if (length < 0 || length > MAX_STRING_BYTES) {
            throw new BridgeFailure("PROTOCOL_ERROR");
        }
        byte[] bytes = stream.readNBytes(length);
        if (bytes.length != length) {
            throw new EOFException();
        }
        try {
            return StandardCharsets.UTF_8.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(bytes))
                .toString();
        } catch (CharacterCodingException failure) {
            throw new BridgeFailure("PROTOCOL_ERROR");
        }
    }

    private static void validateToken(String value, int maximumCharacters) throws BridgeFailure {
        if (value.isEmpty() || value.length() > maximumCharacters
            || !value.matches("[A-Za-z0-9_-]+")) {
            throw new BridgeFailure("PROTOCOL_ERROR");
        }
    }

    private static void validateUrl(String value) throws BridgeFailure {
        if (value.isEmpty() || value.length() > MAX_STRING_BYTES
            || !value.startsWith("file:/")
            || value.indexOf('\r') >= 0
            || value.indexOf('\n') >= 0
            || value.indexOf('\0') >= 0) {
            throw new BridgeFailure("PROTOCOL_ERROR");
        }
    }

    private static void writeSuccess(OutputStream output, Result result) throws IOException {
        DataOutputStream stream = new DataOutputStream(new BufferedOutputStream(output));
        stream.writeInt(RESPONSE_MAGIC);
        stream.writeInt(PROTOCOL_VERSION);
        stream.writeBoolean(true);
        writeString(stream, "OK");
        stream.writeBoolean(result.unoConnectionVerified);
        stream.writeBoolean(result.writerComponentVerified);
        stream.writeBoolean(result.readOnlyVerified);
        stream.writeBoolean(result.hiddenRequested);
        stream.writeBoolean(result.pickListDisabledRequested);
        stream.writeBoolean(result.repairDisabledRequested);
        stream.writeBoolean(result.macroNeverExecuteRequested);
        stream.writeBoolean(result.updateNoUpdateRequested);
        stream.writeBoolean(result.writerPdfExportVerified);
        stream.writeBoolean(result.pdfFilterExplicit);
        stream.writeBoolean(result.overwriteDisabled);
        stream.writeBoolean(result.sourceLocationPreserved);
        stream.writeBoolean(result.documentClosed);
        stream.writeBoolean(result.desktopTerminated);
        stream.writeBoolean(result.localContextReleaseDeferredToProcessExit);
        stream.flush();
    }

    private static void writeTerminalFailure(
        String code,
        boolean documentClosed,
        boolean desktopTerminated,
        boolean localContextReleaseDeferredToProcessExit
    ) {
        try {
            DataOutputStream stream = new DataOutputStream(
                new BufferedOutputStream(System.out)
            );
            stream.writeInt(RESPONSE_MAGIC);
            stream.writeInt(PROTOCOL_VERSION);
            stream.writeBoolean(false);
            writeString(stream, code);
            stream.writeBoolean(documentClosed);
            stream.writeBoolean(desktopTerminated);
            stream.writeBoolean(localContextReleaseDeferredToProcessExit);
            stream.flush();
        } catch (Throwable ignored) {
            // The parent also treats an absent or malformed response as failure.
        }
    }

    private static void writeString(DataOutputStream stream, String value) throws IOException {
        byte[] bytes = value.getBytes(StandardCharsets.UTF_8);
        stream.writeInt(bytes.length);
        stream.write(bytes);
    }

    private static final class Request {
        final String pipeName;
        final String sourceUrl;
        final String outputUrl;
        final String inputFilterName;
        final String pageRange;
        final boolean pdfA1b;
        final boolean exportBookmarks;
        final int connectTimeoutMilliseconds;

        Request(
            String pipeName,
            String sourceUrl,
            String outputUrl,
            String inputFilterName,
            String pageRange,
            boolean pdfA1b,
            boolean exportBookmarks,
            int connectTimeoutMilliseconds
        ) {
            this.pipeName = pipeName;
            this.sourceUrl = sourceUrl;
            this.outputUrl = outputUrl;
            this.inputFilterName = inputFilterName;
            this.pageRange = pageRange;
            this.pdfA1b = pdfA1b;
            this.exportBookmarks = exportBookmarks;
            this.connectTimeoutMilliseconds = connectTimeoutMilliseconds;
        }
    }

    private static final class Result {
        final boolean unoConnectionVerified;
        final boolean writerComponentVerified;
        final boolean readOnlyVerified;
        final boolean hiddenRequested;
        final boolean pickListDisabledRequested;
        final boolean repairDisabledRequested;
        final boolean macroNeverExecuteRequested;
        final boolean updateNoUpdateRequested;
        final boolean writerPdfExportVerified;
        final boolean pdfFilterExplicit;
        final boolean overwriteDisabled;
        final boolean sourceLocationPreserved;
        final boolean documentClosed;
        final boolean desktopTerminated;
        final boolean localContextReleaseDeferredToProcessExit;

        Result(
            boolean unoConnectionVerified,
            boolean writerComponentVerified,
            boolean readOnlyVerified,
            boolean hiddenRequested,
            boolean pickListDisabledRequested,
            boolean repairDisabledRequested,
            boolean macroNeverExecuteRequested,
            boolean updateNoUpdateRequested,
            boolean writerPdfExportVerified,
            boolean pdfFilterExplicit,
            boolean overwriteDisabled,
            boolean sourceLocationPreserved,
            boolean documentClosed,
            boolean desktopTerminated,
            boolean localContextReleaseDeferredToProcessExit
        ) {
            this.unoConnectionVerified = unoConnectionVerified;
            this.writerComponentVerified = writerComponentVerified;
            this.readOnlyVerified = readOnlyVerified;
            this.hiddenRequested = hiddenRequested;
            this.pickListDisabledRequested = pickListDisabledRequested;
            this.repairDisabledRequested = repairDisabledRequested;
            this.macroNeverExecuteRequested = macroNeverExecuteRequested;
            this.updateNoUpdateRequested = updateNoUpdateRequested;
            this.writerPdfExportVerified = writerPdfExportVerified;
            this.pdfFilterExplicit = pdfFilterExplicit;
            this.overwriteDisabled = overwriteDisabled;
            this.sourceLocationPreserved = sourceLocationPreserved;
            this.documentClosed = documentClosed;
            this.desktopTerminated = desktopTerminated;
            this.localContextReleaseDeferredToProcessExit =
                localContextReleaseDeferredToProcessExit;
        }
    }

    private static final class Cleanup {
        final boolean documentClosed;
        final boolean desktopTerminated;

        Cleanup(
            boolean documentClosed,
            boolean desktopTerminated
        ) {
            this.documentClosed = documentClosed;
            this.desktopTerminated = desktopTerminated;
        }
    }

    private static final class BridgeFailure extends Exception {
        final String code;
        final boolean documentClosed;
        final boolean desktopTerminated;
        final boolean localContextReleaseDeferredToProcessExit;

        BridgeFailure(String code) {
            this(code, false, false, false);
        }

        BridgeFailure(
            String code,
            boolean documentClosed,
            boolean desktopTerminated,
            boolean localContextReleaseDeferredToProcessExit
        ) {
            super(code);
            this.code = code;
            this.documentClosed = documentClosed;
            this.desktopTerminated = desktopTerminated;
            this.localContextReleaseDeferredToProcessExit =
                localContextReleaseDeferredToProcessExit;
        }

        BridgeFailure withCleanup(
            boolean documentClosed,
            boolean desktopTerminated,
            boolean localContextReleaseDeferredToProcessExit
        ) {
            return new BridgeFailure(
                code,
                documentClosed,
                desktopTerminated,
                localContextReleaseDeferredToProcessExit
            );
        }
    }
}
