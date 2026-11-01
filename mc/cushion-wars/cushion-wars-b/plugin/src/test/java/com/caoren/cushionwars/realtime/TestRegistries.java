package com.caoren.cushionwars.realtime;

import io.papermc.paper.registry.*;
import io.papermc.paper.registry.tag.*;
import java.lang.reflect.Proxy;
import java.util.*;
import java.util.stream.Stream;
import org.bukkit.*;
import org.bukkit.attribute.Attribute;

/** Minimal offline API registry provider, only used on the test classpath. No Minecraft server. */
@SuppressWarnings("removal")
public final class TestRegistries implements RegistryAccess {
    @Override public <T extends Keyed> Registry<T> getRegistry(Class<T> type) {
        return new Empty<>(type == Attribute.class);
    }
    @Override public <T extends Keyed> Registry<T> getRegistry(RegistryKey<T> key) {
        return new Empty<>(key == RegistryKey.ATTRIBUTE);
    }
    private static final class Empty<T extends Keyed> implements Registry<T> {
        private final boolean attributes;
        private final Map<NamespacedKey, T> values = new HashMap<>();
        Empty(boolean attributes) { this.attributes = attributes; }
        @SuppressWarnings("unchecked")
        public T get(NamespacedKey key) {
            if (!attributes) return null;
            return values.computeIfAbsent(key, k -> (T) Proxy.newProxyInstance(Attribute.class.getClassLoader(),
                    new Class<?>[]{Attribute.class}, (p, m, a) -> switch (m.getName()) {
                        case "getKey" -> k;
                        case "hashCode" -> k.hashCode();
                        case "equals" -> p == a[0];
                        case "toString", "name", "getTranslationKey", "translationKey" -> k.toString();
                        case "ordinal" -> 0;
                        case "compareTo" -> 0;
                        case "getDefaultValue" -> 20d;
                        default -> null;
                    }));
        }
        public NamespacedKey getKey(T value) { return value.getKey(); }
        public boolean hasTag(TagKey<T> key) { return false; }
        public io.papermc.paper.registry.tag.Tag<T> getTag(TagKey<T> key) { throw new NoSuchElementException(); }
        public Collection<T> getTagValues(TagKey<T> key) { return List.of(); }
        public Collection<io.papermc.paper.registry.tag.Tag<T>> getTags() { return List.of(); }
        public Stream<T> stream() { return values.values().stream(); }
        public Stream<NamespacedKey> keyStream() { return values.keySet().stream(); }
        public int size() { return values.size(); }
        public Iterator<T> iterator() { return values.values().iterator(); }
    }
}
