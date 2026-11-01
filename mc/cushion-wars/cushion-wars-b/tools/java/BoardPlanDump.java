import com.caoren.cushionwars.realtime.rules.BoardGeometry;
import com.caoren.cushionwars.realtime.rules.BoardPlan;
import com.caoren.cushionwars.realtime.rules.GridPos;
import com.caoren.cushionwars.realtime.rules.Side;
import com.caoren.cushionwars.realtime.rules.TemplateValidator;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.List;
import java.util.Set;

/** verify_map 调用：输出 1—8 人默认棋盘的生成摘要（NDJSON）。 */
public final class BoardPlanDump {
    public static void main(String[] args) throws Exception {
        for (int n = 1; n <= 8; n++) {
            List<BoardPlan.Cell> cells = BoardPlan.cells(n);
            List<String> blocks = BoardPlan.defaultBlocks(n);
            List<String> errors = TemplateValidator.validate(n, blocks);
            Set<GridPos> covers = BoardGeometry.covers(n);
            int size = BoardGeometry.size(n);
            int half = BoardGeometry.half(n);
            int gold = 0;
            int ground = 0;
            int coverBlocks = 0;
            int airUpper = 0;
            for (BoardPlan.Cell cell : cells) {
                if (cell.y() == 0) {
                    ground++;
                    if ("minecraft:gold_block".equals(cell.material())) {
                        gold++;
                    }
                } else if ("minecraft:stone_bricks".equals(cell.material())) {
                    coverBlocks++;
                } else if ("minecraft:air".equals(cell.material())) {
                    airUpper++;
                }
            }
            boolean symmetric = true;
            for (GridPos pos : covers) {
                symmetric &= covers.contains(new GridPos(-pos.x(), -pos.z()));
            }
            boolean corners = true;
            for (int x : new int[]{-2, 2}) {
                for (int z : new int[]{-2, 2}) {
                    corners &= covers.contains(new GridPos(x, z));
                }
            }
            String hash = sha256(String.join("\n", blocks));
            StringBuilder spawns = new StringBuilder("[");
            for (BoardGeometry.TeamSlot slot : BoardGeometry.spawnSlots(n)) {
                if (spawns.length() > 1) {
                    spawns.append(',');
                }
                spawns.append("{\"side\":\"").append(slot.side().id())
                        .append("\",\"index\":").append(slot.index())
                        .append(",\"x\":").append(slot.x())
                        .append(",\"z\":").append(slot.z()).append('}');
            }
            spawns.append(']');
            StringBuilder ascii = new StringBuilder("[");
            for (String line : BoardPlan.ascii(n, 1)) {
                if (ascii.length() > 1) {
                    ascii.append(',');
                }
                ascii.append('"').append(line).append('"');
            }
            ascii.append(']');
            System.out.println("{\"team\":" + n
                    + ",\"size\":" + size
                    + ",\"half\":" + half
                    + ",\"total\":" + blocks.size()
                    + ",\"covers\":" + covers.size()
                    + ",\"coverBlocks\":" + coverBlocks
                    + ",\"ground\":" + ground
                    + ",\"gold\":" + gold
                    + ",\"upperAir\":" + airUpper
                    + ",\"spawns\":" + spawns
                    + ",\"symmetric\":" + symmetric
                    + ",\"corners\":" + corners
                    + ",\"errors\":" + errors.size()
                    + ",\"hash\":\"" + hash + "\""
                    + ",\"ascii\":" + ascii + "}");
        }
    }

    private static String sha256(String value) throws Exception {
        byte[] digest = MessageDigest.getInstance("SHA-256").digest(value.getBytes(StandardCharsets.UTF_8));
        StringBuilder out = new StringBuilder();
        for (byte b : digest) {
            out.append(String.format("%02x", b));
        }
        return out.toString();
    }
}
